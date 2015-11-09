﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Anathema
{
    partial class GUIDisplay : UserControl
    {
        // Display constants
        private const Int32 MarginSize = 4;
        private const Int32 MaximumDisplayed = 4000;

        public GUIDisplay()
        {
            InitializeComponent();
        }

        public void UpdateMemoryLabels(Snapshot Snapshot)
        {
            List<Object> Labels = Snapshot.GetMemoryLabels();
            Int32 LabelCount = Math.Min(Snapshot.GetMemoryLabels().Count, MaximumDisplayed);
            for (Int32 Index = 0; Index < LabelCount; Index++)
            {
                ListViewItem NextEntry = new ListViewItem(Labels[Index].ToString());
                NextEntry.SubItems.Add(Labels[Index].ToString());
                NextEntry.SubItems.Add("");
                AddressListView.Items.Add(NextEntry);
            }
        }
    }
}