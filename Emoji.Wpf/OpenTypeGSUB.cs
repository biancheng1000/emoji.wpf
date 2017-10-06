﻿//
//  Emoji.Wpf — Emoji support for WPF
//
//  Copyright © 2017 Sam Hocevar <sam@hocevar.net>
//
//  This library is free software. It comes without any warranty, to
//  the extent permitted by applicable law. You can redistribute it
//  and/or modify it under the terms of the Do What the Fuck You Want
//  to Public License, Version 2, as published by the WTFPL Task Force.
//  See http://www.wtfpl.net/ for more details.
//

using System;
using System.Collections.Generic;

namespace Emoji.Wpf
{
    public partial class ColorTypeface
    {
        // Read the GSUB table
        // https://www.microsoft.com/typography/otspec/gsub.htm
        // https://www.microsoft.com/typography/otspec/chapter2.htm
        private void ReadGSUB(BEBinaryReader b, int offset)
        {
            b.SeekAt(offset);

            int major_version = b.ReadUInt16();
            int minor_version = b.ReadUInt16();
            int script_list_offset = b.ReadInt16();
            int feature_list_offset = b.ReadInt16();
            int lookup_list_offset = b.ReadInt16();
            int variations_list_offset = minor_version == 1 ? b.ReadInt32() : 0;

            // Read script list table
            Dictionary<string, int> scripts = new Dictionary<string, int>();
            b.SeekAt(offset + script_list_offset);
            int script_count = b.ReadInt16();
            for (int i = 0; i < script_count; ++i)
            {
                string tag = b.ReadTag();
                scripts[tag] = b.ReadUInt16();
            }

            // Read feature list table
            Dictionary<string, int> features = new Dictionary<string, int>();
            b.SeekAt(offset + feature_list_offset);
            int feature_count = b.ReadInt16();
            for (int i = 0; i < feature_count; ++i)
            {
                string tag = b.ReadTag();
                features[tag] = b.ReadUInt16();
            }

            // Read lookup list table
            b.SeekAt(offset + lookup_list_offset);
            ushort[] lookups = b.ReadUInt16Table(b.ReadUInt16());

            foreach (int lookup_offset in lookups)
                ReadGSUBLookup(b, offset + lookup_list_offset + lookup_offset);
        }

        void ReadGSUBLookup(BEBinaryReader b, int offset)
        {
            b.SeekAt(offset);

            int type = b.ReadUInt16();
            int flag = b.ReadUInt16();
            int[] subtable_offsets = new int[b.ReadUInt16()];
            for (int i = 0; i < subtable_offsets.Length; ++i)
                subtable_offsets[i] = offset + b.ReadUInt16();

            // Extension substitution tables need an extra indirection
            if (type == 7)
            {
                for (int i = 0; i < subtable_offsets.Length; ++i)
                {
                    b.SeekAt(subtable_offsets[i]);
                    b.ReadUInt16(); // must be 1
                    type = b.ReadUInt16(); // must all be the same
                    subtable_offsets[i] += b.ReadInt32();
                }
            }

            foreach (int subtable_offset in subtable_offsets)
            {
                switch (type)
                {
                case 1: ReadGSUBLookupType1(b, subtable_offset); break;
                case 4: ReadGSUBLookupType4(b, subtable_offset); break;
                case 6: ReadGSUBLookupType6(b, subtable_offset); break;
                default: break; // FIXME: unsupported for now
                }
            }
        }

        private List<LigatureDef> m_ligatures = new List<LigatureDef>();

        private class LigatureDef
        {
            internal struct List
            {
                internal struct Item
                {
                    public ushort[] Sequence { get; set; }
                    public ushort Result { get; set; }
                }

                public Item[] Ligatures;
            }

            public Coverage Coverage { get; set; }
            public List[] Rules { get; set; }
        }

        private class Coverage
        {
            public Coverage(BEBinaryReader b, int offset)
            {
                b.SeekAt(offset);
                m_format = b.ReadUInt16();

                if (m_format == 1)
                    m_data = b.ReadUInt16Table(b.ReadUInt16());
                else if (m_format == 2)
                    m_data = b.ReadUInt16Table(3 * b.ReadUInt16());
            }

            private int GetCoverageIndex(ushort gid)
            {
                if (m_format == 1)
                    return Array.BinarySearch(m_data, gid);

                if (m_format == 2)
                    for (int i = 0; i < m_data.Length; i += 3)
                        if (m_data[i] <= gid && gid <= m_data[i + 1])
                            return gid - m_data[i] + m_data[i + 2];

                return -1;
            }

            private int m_format;
            private ushort[] m_data;
        }

        // LookupType 1: Single Substitution Subtable
        void ReadGSUBLookupType1(BEBinaryReader b, int offset)
        {
            b.SeekAt(offset);

            int format = b.ReadUInt16();
            int coverage_offset = offset + b.ReadUInt16();
            if (format == 1)
            {
                // Single Substitution Format 1
                int delta = b.ReadUInt16();
                Coverage cov = new Coverage(b, coverage_offset);
            }
            else if (format == 2)
            {
                // Single Substitution Format 2
                ushort[] substitutes = b.ReadUInt16Table(b.ReadUInt16());
                Coverage cov = new Coverage(b, coverage_offset);
            }

            // FIXME: store the substitution information somewhere!
        }

        // LookupType 4: Ligature Substitution Subtable
        void ReadGSUBLookupType4(BEBinaryReader b, int offset)
        {
            b.SeekAt(offset);

            int format = b.ReadUInt16();
            int coverage_offset = offset + b.ReadUInt16();
            if (format == 1)
            {
                // Ligature Substitution Format 1
                LigatureDef lig = new LigatureDef();

                ushort[] ligset_offsets = b.ReadUInt16Table(b.ReadUInt16());

                lig.Coverage = new Coverage(b, coverage_offset);
                lig.Rules = new LigatureDef.List[ligset_offsets.Length];

                // There are as many ligature sets as glyphs in the coverage
                for (int i = 0; i < ligset_offsets.Length; ++i)
                {
                    int ligset_offset = offset + ligset_offsets[i];

                    b.SeekAt(ligset_offset);
                    ushort[] lig_offsets = b.ReadUInt16Table(b.ReadUInt16());

                    lig.Rules[i].Ligatures = new LigatureDef.List.Item[lig_offsets.Length];

                    // All possible ligatures starting with this glyph
                    for (int j = 0; j < lig_offsets.Length; ++j)
                    {
                        b.SeekAt(ligset_offset + lig_offsets[j]);
                        lig.Rules[i].Ligatures[j].Result = b.ReadUInt16();
                        // The implicit 0th element of the ligature is the ith
                        // glyph in the coverage.
                        ushort[] sequence = b.ReadUInt16Table(b.ReadUInt16() - 1);
                        lig.Rules[i].Ligatures[j].Sequence = sequence;
                    }
                }

                m_ligatures.Add(lig);
            }
        }

        void ReadGSUBLookupType6(BEBinaryReader b, int offset)
        {
            b.SeekAt(offset);

            int format = b.ReadUInt16();
            if (format == 1)
            {
                // FIXME: unsupported
            }
            else if (format == 2)
            {
                // FIXME: unsupported
            }
            else if (format == 3)
            {
                ushort[] backtrack_offsets = b.ReadUInt16Table(b.ReadUInt16());
                ushort[] input_offsets = b.ReadUInt16Table(b.ReadUInt16());
                ushort[] lookahead_offsets = b.ReadUInt16Table(b.ReadUInt16());

                int substitution_count = b.ReadUInt16();
                for (int i = 0; i < substitution_count; ++i)
                {
                    int sequence_index = b.ReadUInt16();
                    int lookup_index = b.ReadUInt16();
                }

                Coverage[] backtracks = new Coverage[backtrack_offsets.Length];
                Coverage[] inputs = new Coverage[input_offsets.Length];
                Coverage[] lookaheads = new Coverage[lookahead_offsets.Length];

                for (int i = 0; i < backtrack_offsets.Length; ++i)
                    backtracks[i] = new Coverage(b, offset + backtrack_offsets[i]);

                for (int i = 0; i < input_offsets.Length; ++i)
                    inputs[i] = new Coverage(b, offset + input_offsets[i]);

                for (int i = 0; i < lookahead_offsets.Length; ++i)
                    lookaheads[i] = new Coverage(b, offset + lookahead_offsets[i]);
            }
        }
    }
}
