﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Binarysharp.MemoryManagement;
using Binarysharp.MemoryManagement.Memory;
using System.Collections.Concurrent;

namespace Anathema
{
    class ChunkScanner : IChunkScannerModel
    {
        private List<MemoryChunkRoots> ChunkRoots;
        private Snapshot<Null> Snapshot;
        private Int32 ChunkSize;

        // User controlled variables
        private Int32 MinChanges;

        public ChunkScanner()
        {

        }

        public override void SetMinChanges(Int32 MinChanges)
        {
            this.MinChanges = MinChanges;
        }

        /// <summary>
        /// Heuristic algorithm to determine the proper chunk size for this chunk scan based on the current region size.
        /// Chunks vary in powers of 2 from 32B to 1024B
        /// </summary>
        /// <param name="MemorySize"></param>
        /// <returns></returns>
        private Int32 SetChunkSize(UInt64 MemorySize)
        {
            UInt64 MB = (UInt64)(MemorySize >> 20);
            Int32 MBBits = 0;
            while ((MB >>= 1) != 0) { MBBits++; }
            MBBits = (MBBits <= 5 ? 5 : (MBBits >= 10 ? 10 : MBBits));
            return (Int32)(1 << MBBits);
        }

        public override void Begin()
        {
            this.Snapshot = new Snapshot<Null>(SnapshotManager.GetInstance().GetActiveSnapshot(true));
            this.Snapshot.SetElementType(typeof(SByte));
            this.ChunkRoots = new List<MemoryChunkRoots>();
            this.ChunkSize = SetChunkSize(Snapshot.GetMemorySize());

            foreach (SnapshotRegion SnapshotRegion in Snapshot)
                ChunkRoots.Add(new MemoryChunkRoots(SnapshotRegion, ChunkSize));

            base.Begin();
        }

        protected override void Update()
        {
            base.Update();

            MemoryEditor MemoryEditor = Snapshot.GetMemoryEditor();

            Parallel.ForEach(ChunkRoots, (ChunkRoot) =>
            {
                try
                {
                    // Process the changes that have occurred since the last sampling for this memory page
                    ChunkRoot.ProcessChanges(ChunkRoot.ReadAllSnapshotMemory(MemoryEditor, false));
                }
                catch (ScanFailedException)
                {
                    // Fuck it
                }
            });

            OnEventUpdateScanCount(new ScannerEventArgs(this.ScanCount));
        }

        public override void End()
        {
            // Wait for the filter to finish
            base.End();
            
            // Collect the pages that have changed
            List<SnapshotRegion> FilteredRegions = new List<SnapshotRegion>();
            for (Int32 Index = 0; Index < ChunkRoots.Count; Index++)
                ChunkRoots[Index].GetChangedRegions(FilteredRegions, MinChanges);

            // Create snapshot with results
            Snapshot<Null> FilteredSnapshot = new Snapshot<Null>(FilteredRegions.ToArray());

            // Grow regions by the size of the largest standard variable and mask this with the original memory list.
            FilteredSnapshot.ExpandAllRegionsOutward(sizeof(UInt64) - 1);
            FilteredSnapshot = new Snapshot<Null>(FilteredSnapshot.MaskRegions(Snapshot, FilteredSnapshot.GetSnapshotRegions()));

            // Read memory so that there are values for the next scan to process
            FilteredSnapshot.ReadAllSnapshotMemory();
            FilteredSnapshot.SetScanMethod("Chunk Scan");

            // Save result
            SnapshotManager.GetInstance().SaveSnapshot(FilteredSnapshot);

            CleanUp();
        }

        private void CleanUp()
        {
            ChunkRoots = null;
            Snapshot = null;
        }

        public class MemoryChunkRoots : SnapshotRegion<Null>
        {
            private SnapshotRegion[] Chunks;
            private UInt16[] ChangeCounts;
            private UInt64?[] Checksums;

            public MemoryChunkRoots(SnapshotRegion Region, Int32 ChunkSize) : base(Region.BaseAddress, Region.RegionSize)
            {
                // Initialize state variables
                Int32 ChunkCount = RegionSize / ChunkSize + (RegionSize % ChunkSize == 0 ? 0 : 1);
                IntPtr CurrentBase = Region.BaseAddress;
                Chunks = new SnapshotRegion[ChunkCount];
                ChangeCounts = new UInt16[ChunkCount];
                Checksums = new UInt64?[ChunkCount];

                // Initialize all chunk properties
                for (Int32 ChunkIndex = 0; ChunkIndex < ChunkCount; ChunkIndex++)
                {
                    Int32 ChunkRegionSize = ChunkSize;

                    if (RegionSize % ChunkSize != 0 && ChunkIndex == ChunkCount - 1)
                        ChunkRegionSize = RegionSize % ChunkSize;

                    Chunks[ChunkIndex] = new SnapshotRegion<Null>(CurrentBase, ChunkRegionSize);
                    ChangeCounts[ChunkIndex] = 0;

                    CurrentBase += ChunkSize;
                }
            }

            /// <summary>
            /// Collects the chunks that have changed at least as many times as the specified minimum
            /// </summary>
            /// <param name="AcceptedRegions"></param>
            /// <param name="MinChanges"></param>
            public void GetChangedRegions(List<SnapshotRegion> AcceptedRegions, Int32 MinChanges)
            {
                for (Int32 ChunkIndex = 0; ChunkIndex < Chunks.Length; ChunkIndex++)
                {
                    if (ChangeCounts[ChunkIndex] >= MinChanges)
                        AcceptedRegions.Add(Chunks[ChunkIndex]);
                }
            }
            
            /// <summary>
            /// Processes a chunk of data to determine if it has changed
            /// </summary>
            /// <param name="Data"></param>
            public void ProcessChanges(Byte[] Data)
            {
                for (Int32 ChunkIndex = 0; ChunkIndex < Chunks.Length; ChunkIndex++)
                {
                    UInt64 NewChecksum = Hashing.ComputeCheckSum(Data,
                        (UInt32)((UInt64)Chunks[ChunkIndex].BaseAddress - (UInt64)this.BaseAddress),
                        (UInt32)((UInt64)Chunks[ChunkIndex].EndAddress - (UInt64)this.BaseAddress));

                    if (Checksums[ChunkIndex].HasValue)
                    {
                        // Increment change count for this chunk if the new checksum differs
                        if (Checksums[ChunkIndex] != NewChecksum)
                            ChangeCounts[ChunkIndex]++;
                    }
                    else
                    {
                        // Initialize change count
                        ChangeCounts[ChunkIndex] = 0;
                    }
                    
                    Checksums[ChunkIndex] = NewChecksum;
                }
            }

        } // End class

    } // End class

} // End namespace