﻿// -----------------------------------------------------------
// This program is private software, based on C# source code.
// To sell or change credits of this software is forbidden,
// except if someone approves it from the Blaze INC. team.
// -----------------------------------------------------------
// Copyrights (c) 2016 Blaze.Server INC. All rights reserved.
// -----------------------------------------------------------

#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Blaze.Server.Base;
using Blaze.Server.Logging;
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedMember.Local

#endregion

namespace Blaze.Server.Blaze
{
    internal sealed class TdfEncoder
    {
        private readonly List<Tdf> _data;
        private readonly MemoryStream _stream;

        public TdfEncoder()
        {
            _stream = new MemoryStream();
        }

        public TdfEncoder(List<Tdf> data)
        {
            _stream = new MemoryStream();
            _data = data;
        }

        /// <summary>
        ///     Encodes a label to tag.
        /// </summary>
        /// <param name="label">Label to write.</param>
        private void WriteLabel(string label)
        {
            // by Pedro Martins
            var tag = 0;

            for (var i = 0; i < label.Length; i++)
            {
                tag |= (0x20 | (label[i] & 0x1F)) << (3 - i)*6;
            }

            var sTag = Utils.SwapBytes(Convert.ToUInt32(tag)) >> 8;

            // hackhackhack
            // FIXME
            var hehStream = new MemoryStream();
            var hehWriter = new BinaryWriter(hehStream);
            hehWriter.Write(sTag);

            _stream.Write(hehStream.GetBuffer().Take(3).ToArray(), 0, 3);
        }

        private void WriteInteger(ulong value)
        {
            if (value < 0x40)
            {
                _stream.WriteByte((byte) (value & 0xFF));
            }
            else
            {
                _stream.WriteByte((byte) ((value & 0x3F) | 0x80));
                var currshift = value >> 6;

                while (currshift >= 0x80)
                {
                    _stream.WriteByte((byte) ((currshift & 0x7F) | 0x80));
                    currshift >>= 7;
                }

                _stream.WriteByte((byte) currshift);
            }
        }

        private void WriteString(string value)
        {
            var stringBytes = Encoding.ASCII.GetBytes(value);
            var length = (uint) value.Length;

            // write string length
            WriteInteger(length + 1);

            // write string
            _stream.Write(stringBytes, 0, (int) length);

            // write end byte
            _stream.WriteByte(0);
        }

        private void WriteBlob(IReadOnlyList<byte> data)
        {
            var length = (uint) data.Count;

            // write lenght
            WriteInteger(length);

            // write data
            for (var i = 0; i < length; i++)
            {
                _stream.WriteByte(data[i]);
            }
        }

        private void WriteStruct(List<Tdf> data)
        {
            // write struct items
            data.ForEach(WriteTdf);

            // write end byte
            _stream.WriteByte(0);
        }

        private void WriteList(TdfBaseType type, IEnumerable list)
        {
            foreach (var obj in list)
            {
                switch (type)
                {
                    case TdfBaseType.Integer:
                        WriteInteger((ulong) obj);
                        break;

                    case TdfBaseType.String:
                        WriteString((string) obj);
                        break;

                    case TdfBaseType.Struct:
                        WriteStruct((List<Tdf>) obj);
                        break;

                    case TdfBaseType.TDF_TYPE_BLAZE_OBJECT_ID:
                        WriteTdfVector3((TdfVector3) obj);
                        break;

                    default:
                        Log.Warn($"Unknown list type: {type}.");
                        break;
                }
            }
        }

        private void WriteTdfMin(TdfMin tdf) => _stream.WriteByte((byte) tdf.Value);

        private void WriteTdfInteger(TdfInteger tdf) => WriteInteger(tdf.Value);

        private void WriteTdfList(TdfList tdf)
        {
            // write list type
            _stream.WriteByte((byte) tdf.ListType);

            // write list size
            _stream.WriteByte((byte) tdf.List.Count);

            if (tdf.Stub)
            {
                _stream.WriteByte(2);
            }

            // write list
            WriteList(tdf.ListType, tdf.List);
        }

        private void WriteTdfMap(TdfMap tdf)
        {
            // write list types
            _stream.WriteByte((byte) tdf.KeyType);
            _stream.WriteByte((byte) tdf.ValueType);

            // write list size
            _stream.WriteByte((byte) tdf.Map.Count);

            // write map
            Action<TdfBaseType, object> writeListItem = (type, item) =>
            {
                switch (type)
                {
                    case TdfBaseType.Integer:
                        WriteInteger((ulong) item);
                        break;

                    case TdfBaseType.String:
                        WriteString((string) item);
                        break;

                    case TdfBaseType.Struct:
                        WriteStruct((List<Tdf>) item);
                        break;

                    default:
                        Log.Warn($"Unknown list item type: {type}");
                        break;
                }
            };

            foreach (var item in tdf.Map)
            {
                writeListItem(tdf.KeyType, item.Key);
                writeListItem(tdf.ValueType, item.Value);
            }
        }

        private void WriteTdfUnion(TdfUnion tdf)
        {
            // write active member
            _stream.WriteByte((byte) tdf.activeMember);

            if (tdf.activeMember != NetworkAddressMember.Unset)
            {
                tdf.Data.ForEach(WriteTdf);
            }
        }

        private void WriteTdfIntegerList(TdfIntegerList tdf)
        {
            // write list size
            _stream.WriteByte((byte) tdf.list.Count);

            // write list
            foreach (var l in tdf.list)
            {
                WriteInteger(l);
            }
        }

        private void WriteTdfVector2(TdfVector2 tdf)
        {
            WriteInteger(tdf.Value1);
            WriteInteger(tdf.Value2);
        }

        private void WriteTdfVector3(TdfVector3 tdf)
        {
            WriteInteger(tdf.Value1);
            WriteInteger(tdf.Value2);
            WriteInteger(tdf.Value3);
        }

        private void WriteTdf(Tdf tdf)
        {
            // write label
            WriteLabel(tdf.Label);

            // write type
            _stream.WriteByte((byte) tdf.Type);

            switch (tdf.Type)
            {
                case TdfBaseType.Integer:
                    WriteTdfInteger((TdfInteger) tdf);
                    break;

                case TdfBaseType.String:
                    WriteString(((TdfString) tdf).Value);
                    break;

                case TdfBaseType.Binary:
                    WriteBlob(((TdfBlob) tdf).Data);
                    break;

                case TdfBaseType.Struct:
                    WriteStruct(((TdfStruct) tdf).Data);
                    break;

                case TdfBaseType.List:
                    WriteTdfList((TdfList) tdf);
                    break;

                case TdfBaseType.Map:
                    WriteTdfMap((TdfMap) tdf);
                    break;

                case TdfBaseType.Union:
                    WriteTdfUnion((TdfUnion) tdf);
                    break;

                case TdfBaseType.Variable:
                    WriteTdfIntegerList((TdfIntegerList) tdf);
                    break;

                case TdfBaseType.TDF_TYPE_BLAZE_OBJECT_TYPE:
                    WriteTdfVector2((TdfVector2) tdf);
                    break;

                case TdfBaseType.TDF_TYPE_BLAZE_OBJECT_ID:
                    WriteTdfVector3((TdfVector3) tdf);
                    break;

                default:
                    Log.Warn($"Unknown Tdf type: {tdf.Type}");
                    break;
            }
        }

        private void WriteTdf(List<Tdf> tdfs) => tdfs.ForEach(WriteTdf);

        public byte[] Encode()
        {
            if (_data != null) WriteTdf(_data);

            var buffer = _stream.GetBuffer();
            var position = (int) _stream.Position;

            return buffer.Take(position).ToArray();
        }
    }
}