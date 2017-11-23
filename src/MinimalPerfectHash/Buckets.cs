/* ........................................................................ *
 * (c) 2010 Laurent Dupuis (www.dupuis.me)                                  *
 * ........................................................................ *
 * < This program is free software: you can redistribute it and/or modify
 * < it under the terms of the GNU General Public License as published by
 * < the Free Software Foundation, either version 3 of the License, or
 * < (at your option) any later version.
 * < 
 * < This program is distributed in the hope that it will be useful,
 * < but WITHOUT ANY WARRANTY; without even the implied warranty of
 * < MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * < GNU General Public License for more details.
 * < 
 * < You should have received a copy of the GNU General Public License
 * < along with this program.  If not, see <http://www.gnu.org/licenses/>.
 * ........................................................................ */

using System;

namespace MinimalPerfectHash
{
	internal struct BucketSortedList
	{
		public UInt32 BucketsList;
		public UInt32 Size;
	}

	internal class Buckets
	{
		private const UInt32 KeysPerBucket = 4; // average number of keys per bucket
		private const UInt32 MaxProbesBase = 1 << 20;

		struct Item
		{
			public UInt32 F;
			public UInt32 H;
		}

		struct Bucket
		{
			public UInt32 ItemsList; // offset
			UInt32 sizeBucketId;

			public UInt32 Size {
				get => sizeBucketId;
				set => sizeBucketId = value;
			}
			public UInt32 BucketId {
				get => sizeBucketId;
				set => sizeBucketId = value;
			}
		}

		struct MapItem
		{
			public UInt32 F;
			public UInt32 H;
			public UInt32 BucketNum;
		};


		Bucket[] buckets;
		Item[] items;
		readonly UInt32 nbuckets;       // number of buckets
		readonly UInt32 n;              // number of bins
		readonly UInt32 m;              // number of keys
		readonly IKeySource keySource;

		public UInt32 NBuckets => nbuckets;

		public UInt32 N => n;

		public Buckets(IKeySource keySource, Double c)
		{
			this.keySource = keySource;

			var loadFactor = c;
			m = keySource.NbKeys;
			nbuckets = m / KeysPerBucket + 1;

			if (loadFactor < 0.5)
				loadFactor = 0.5;
			if (loadFactor >= 0.99)
				loadFactor = 0.99;

			n = (UInt32)(m / (loadFactor)) + 1;

			if (n % 2 == 0)
				n++;
			for (; ; )
			{
				if (MillerRabin.CheckPrimality(n))
					break;
				n += 2; // just odd numbers can be primes for n > 2
			}

			buckets = new Bucket[nbuckets];
			items = new Item[m];

		}

		Boolean BucketsInsert(MapItem[] mapItems, UInt32 itemIdx)
		{
			var bucketIdx = mapItems[itemIdx].BucketNum;
			var p = buckets[bucketIdx].ItemsList;

			for (UInt32 i = 0; i < buckets[bucketIdx].Size; i++)
			{
				if (items[p].F == mapItems[itemIdx].F && items[p].H == mapItems[itemIdx].H)
				{
					return false;
				}
				p++;
			}
			items[p].F = mapItems[itemIdx].F;
			items[p].H = mapItems[itemIdx].H;
			buckets[bucketIdx].Size++;
			return true;
		}

		void BucketsClean()
		{
			for (UInt32 i = 0; i < nbuckets; i++)
				buckets[i].Size = 0;
		}

		public Boolean MappingPhase(out UInt32 hashSeed, out UInt32 maxBucketSize)
		{
			var hl = new UInt32[3];
			var mapItems = new MapItem[m];
			UInt32 mappingIterations = 1000;
			var rdm = new Random(111);

			maxBucketSize = 0;
			for (; ; )
			{
				mappingIterations--;
				hashSeed = (UInt32)rdm.Next((Int32)m); // ((cmph_uint32)rand() % this->_m);

				BucketsClean();

				keySource.Rewind();

				UInt32 i;
				for (i = 0; i < m; i++)
				{
					JenkinsHash.HashVector(hashSeed, keySource.Read(), hl);

					UInt32 g = hl[0] % nbuckets;
					mapItems[i].F = hl[1] % n;
					mapItems[i].H = hl[2] % (n - 1) + 1;
					mapItems[i].BucketNum = g;

					buckets[g].Size++;
					if (buckets[g].Size > maxBucketSize)
					{
						maxBucketSize = buckets[g].Size;
					}
				}
				buckets[0].ItemsList = 0;
				for (i = 1; i < nbuckets; i++)
				{
					buckets[i].ItemsList = buckets[i - 1].ItemsList + buckets[i - 1].Size;
					buckets[i - 1].Size = 0;
				}
				buckets[i - 1].Size = 0;
				for (i = 0; i < m; i++)
				{
					if (!BucketsInsert(mapItems, i))
						break;
				}
				if (i == m)
				{
					return true; // SUCCESS
				}

				if (mappingIterations == 0)
				{
					return false;
				}
			}
		}

		public BucketSortedList[] OrderingPhase(UInt32 maxBucketSize)
		{
			var sortedLists = new BucketSortedList[maxBucketSize + 1];
			var inputBuckets = buckets;
			var inputItems = items;
			UInt32 i;
			UInt32 bucketSize, position;

			for (i = 0; i < nbuckets; i++)
			{
				bucketSize = inputBuckets[i].Size;
				if (bucketSize == 0)
					continue;
				sortedLists[bucketSize].Size++;
			}

			sortedLists[1].BucketsList = 0;
			// Determine final position of list of buckets into the contiguous array that will store all the buckets
			for (i = 2; i <= maxBucketSize; i++)
			{
				sortedLists[i].BucketsList = sortedLists[i - 1].BucketsList + sortedLists[i - 1].Size;
				sortedLists[i - 1].Size = 0;
			}

			sortedLists[i - 1].Size = 0;
			// Store the buckets in a new array which is sorted by bucket sizes
			var outputBuckets = new Bucket[nbuckets];

			for (i = 0; i < nbuckets; i++)
			{
				bucketSize = inputBuckets[i].Size;
				if (bucketSize == 0)
				{
					continue;
				}

				position = sortedLists[bucketSize].BucketsList + sortedLists[bucketSize].Size;
				outputBuckets[position].BucketId = i;
				outputBuckets[position].ItemsList = inputBuckets[i].ItemsList;
				sortedLists[bucketSize].Size++;
			}

			buckets = outputBuckets;

			// Store the items according to the new order of buckets.
			var outputItems = new Item[n];
			position = 0;

			for (bucketSize = 1; bucketSize <= maxBucketSize; bucketSize++)
			{
				for (i = sortedLists[bucketSize].BucketsList;
					 i < sortedLists[bucketSize].Size + sortedLists[bucketSize].BucketsList;
					 i++)
				{
					var position2 = outputBuckets[i].ItemsList;
					outputBuckets[i].ItemsList = position;
					for (UInt32 j = 0; j < bucketSize; j++)
					{
						outputItems[position].F = inputItems[position2].F;
						outputItems[position].H = inputItems[position2].H;
						position++;
						position2++;
					}
				}
			}

			//Return the items sorted in new order and free the old items sorted in old order
			items = outputItems;
			return sortedLists;
		}

		Boolean PlaceBucketProbe(UInt32 probe0Num, UInt32 probe1Num, UInt32 bucketNum, UInt32 size, BitArray occupTable)
		{
			UInt32 i;
			UInt32 position;

			var p = buckets[bucketNum].ItemsList;

			// try place bucket with probe_num
			for (i = 0; i < size; i++) // placement
			{
				position = (UInt32)((items[p].F + ((UInt64)items[p].H) * probe0Num + probe1Num) % n);
				if (occupTable.GetBit(position))
				{
					break;
				}
				occupTable.SetBit(position);
				p++;
			}
			if (i != size) // Undo the placement
			{
				p = buckets[bucketNum].ItemsList;
				for (; ; )
				{
					if (i == 0)
					{
						break;
					}
					position = (UInt32)((items[p].F + ((UInt64)items[p].H) * probe0Num + probe1Num) % n);
					occupTable.UnSetBit(position);

					// 				([position/32]^=(1<<(position%32));
					p++;
					i--;
				}
				return false;
			}
			return true;
		}

		public Boolean SearchingPhase(UInt32 maxBucketSize, BucketSortedList[] sortedLists, UInt32[] dispTable)
		{
			var maxProbes = (UInt32)(((Math.Log(m) / Math.Log(2.0)) / 20) * MaxProbesBase);
			UInt32 i;
			var occupTable = new BitArray((Int32)(((n + 31) / 32) * sizeof(UInt32)));

			for (i = maxBucketSize; i > 0; i--)
			{
				UInt32 probeNum = 0;
				UInt32 probe0Num = 0;
				UInt32 probe1Num = 0;
				var sortedListSize = sortedLists[i].Size;
				while (sortedLists[i].Size != 0)
				{
					var currBucket = sortedLists[i].BucketsList;
					UInt32 nonPlacedBucket = 0;
					for (UInt32 j = 0; j < sortedLists[i].Size; j++)
					{
						// if bucket is successfully placed remove it from list
						if (PlaceBucketProbe(probe0Num, probe1Num, currBucket, i, occupTable))
						{
							dispTable[buckets[currBucket].BucketId] = probe0Num + probe1Num * n;
						}
						else
						{
							buckets[nonPlacedBucket + sortedLists[i].BucketsList].ItemsList = buckets[currBucket].ItemsList;
							buckets[nonPlacedBucket + sortedLists[i].BucketsList].BucketId = buckets[currBucket].BucketId;
							nonPlacedBucket++;
						}
						currBucket++;
					}
					sortedLists[i].Size = nonPlacedBucket;
					probe0Num++;
					if (probe0Num >= n)
					{
						probe0Num -= n;
						probe1Num++;
					}
					probeNum++;
					if (probeNum < maxProbes && probe1Num < n)
						continue;
					sortedLists[i].Size = sortedListSize;
					return false;
				}
				sortedLists[i].Size = sortedListSize;
			}
			return true;
		}
	}
}