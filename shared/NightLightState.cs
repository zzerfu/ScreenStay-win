using System;
using System.IO;
using Microsoft.Win32;

namespace KeepAwake.Shared
{
    /// <summary>
    /// 读取 Windows 夜间模式（Night Light）的当前开关状态。
    /// 数据位于 CloudStore 的 Bond CompactBinary v1 二进制中：
    /// 外层信封 -> 内层 payload -> 内层结构；字段 0（INT32）存在表示夜间模式已开启。
    /// 参考：win-nightlight-cli 的 nightlight registry format 文档。
    /// </summary>
    internal static class NightLightState
    {
        private const string StateKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

        private const string DataValueName = "Data";

        /// <summary>当前夜间模式是否开启；读取失败返回 false。</summary>
        internal static bool TryIsOn(out bool on)
        {
            on = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StateKeyPath, false))
                {
                    if (key == null) return false;
                    byte[] data = key.GetValue(DataValueName) as byte[];
                    if (data == null || data.Length < 8) return false;
                    return TryParseState(data, out on);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>解析夜间模式 State 二进制数据。字段 0 存在 = 开启。</summary>
        internal static bool TryParseState(byte[] data, out bool on)
        {
            on = false;
            try
            {
                byte[] inner = ExtractInnerPayload(data);
                if (inner == null) return false;

                BondReader reader = new BondReader(inner, 0);
                reader.ReadMarshaledHeader();

                bool foundFieldZero = false;
                while (true)
                {
                    int type;
                    int fieldId = reader.ReadFieldHeader(out type);
                    if (fieldId < 0) break;
                    if (fieldId == 0) foundFieldZero = true;
                    reader.SkipValue(type);
                }
                on = foundFieldZero;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>从 CloudStore 外层信封中取出内层 Bond payload。</summary>
        private static byte[] ExtractInnerPayload(byte[] data)
        {
            BondReader reader = new BondReader(data, 0);
            reader.ReadMarshaledHeader();

            while (true)
            {
                int type;
                int fieldId = reader.ReadFieldHeader(out type);
                if (fieldId < 0) break;

                if (fieldId == 1 && type == BondReader.TypeStruct)
                {
                    // 字段 1：payload 容器
                    while (true)
                    {
                        int innerType;
                        int innerFieldId = reader.ReadFieldHeader(out innerType);
                        if (innerFieldId < 0) break;

                        if (innerFieldId == 1 && innerType == BondReader.TypeStruct)
                        {
                            // 字段 1：data wrapper
                            while (true)
                            {
                                int wrapperType;
                                int wrapperFieldId = reader.ReadFieldHeader(out wrapperType);
                                if (wrapperFieldId < 0) break;

                                if (wrapperFieldId == 1 && wrapperType == BondReader.TypeList)
                                {
                                    int elementType = reader.ReadByte() & 0x1F;
                                    int count = reader.ReadVarint();
                                    if (elementType == BondReader.TypeInt8)
                                    {
                                        byte[] payload = new byte[count];
                                        Array.Copy(reader.Data, reader.Position, payload, 0, count);
                                        return payload;
                                    }
                                    for (int i = 0; i < count; i++) reader.SkipValue(elementType);
                                }
                                else
                                {
                                    reader.SkipValue(wrapperType);
                                }
                            }
                        }
                        else
                        {
                            reader.SkipValue(innerType);
                        }
                    }
                }
                else
                {
                    reader.SkipValue(type);
                }
            }
            return null;
        }
    }

    /// <summary>Bond CompactBinary v1 的最小读取器（只实现跳过 / 判断所需的类型）。</summary>
    internal sealed class BondReader
    {
        internal const int TypeStop = 0;
        internal const int TypeStopBase = 1;
        internal const int TypeBool = 2;
        internal const int TypeUInt8 = 3;
        internal const int TypeUInt16 = 4;
        internal const int TypeUInt32 = 5;
        internal const int TypeUInt64 = 6;
        internal const int TypeFloat = 7;
        internal const int TypeDouble = 8;
        internal const int TypeString = 9;
        internal const int TypeStruct = 10;
        internal const int TypeList = 11;
        internal const int TypeSet = 12;
        internal const int TypeMap = 13;
        internal const int TypeInt8 = 14;
        internal const int TypeInt16 = 15;
        internal const int TypeInt32 = 16;
        internal const int TypeInt64 = 17;
        internal const int TypeWString = 18;

        private readonly byte[] _data;
        private int _position;

        internal BondReader(byte[] data, int offset)
        {
            _data = data;
            _position = offset;
        }

        internal byte[] Data
        {
            get { return _data; }
        }

        internal int Position
        {
            get { return _position; }
        }

        internal void ReadMarshaledHeader()
        {
            if (_position + 4 > _data.Length) throw new InvalidDataException("truncated header");
            if (_data[_position] != 0x43 || _data[_position + 1] != 0x42 ||
                _data[_position + 2] != 0x01 || _data[_position + 3] != 0x00)
            {
                throw new InvalidDataException("not Bond CompactBinary v1");
            }
            _position += 4;
        }

        internal int ReadByte()
        {
            if (_position >= _data.Length) throw new InvalidDataException("unexpected end of data");
            return _data[_position++];
        }

        internal int ReadVarint()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                int b = ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 63) throw new InvalidDataException("varint too long");
            }
            return unchecked((int)result);
        }

        internal long ReadVarint64()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                int b = ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 63) throw new InvalidDataException("varint too long");
            }
            return unchecked((long)result);
        }

        /// <summary>读取字段头；返回 -1 表示 BT_STOP（结构结束）。</summary>
        internal int ReadFieldHeader(out int type)
        {
            int b = ReadByte();
            int idBits = (b >> 5) & 0x07;
            type = b & 0x1F;
            if (type == TypeStop) return -1;
            if (idBits <= 5) return idBits;
            if (idBits == 6) return ReadByte();
            int low = ReadByte();
            int high = ReadByte();
            return low | (high << 8);
        }

        internal void SkipValue(int type)
        {
            switch (type)
            {
                case TypeStop:
                case TypeStopBase:
                    return;
                case TypeBool:
                case TypeUInt8:
                case TypeInt8:
                    _position += 1;
                    break;
                case TypeUInt16:
                case TypeUInt32:
                case TypeUInt64:
                case TypeInt16:
                case TypeInt32:
                case TypeInt64:
                    ReadVarint();
                    break;
                case TypeFloat:
                    _position += 4;
                    break;
                case TypeDouble:
                    _position += 8;
                    break;
                case TypeString:
                {
                    int length = ReadVarint();
                    _position += length;
                    break;
                }
                case TypeWString:
                {
                    int count = ReadVarint();
                    _position += count * 2;
                    break;
                }
                case TypeStruct:
                {
                    while (true)
                    {
                        int fieldType;
                        int fieldId = ReadFieldHeader(out fieldType);
                        if (fieldId < 0) break;
                        SkipValue(fieldType);
                    }
                    break;
                }
                case TypeList:
                case TypeSet:
                {
                    int elementType = ReadByte() & 0x1F;
                    int count = ReadVarint();
                    for (int i = 0; i < count; i++) SkipValue(elementType);
                    break;
                }
                case TypeMap:
                {
                    int keyType = ReadByte() & 0x1F;
                    int valueType = ReadByte() & 0x1F;
                    int count = ReadVarint();
                    for (int i = 0; i < count; i++)
                    {
                        SkipValue(keyType);
                        SkipValue(valueType);
                    }
                    break;
                }
                default:
                    throw new InvalidDataException("unsupported Bond type " + type);
            }

            if (_position > _data.Length) throw new InvalidDataException("unexpected end of data");
        }
    }
}
