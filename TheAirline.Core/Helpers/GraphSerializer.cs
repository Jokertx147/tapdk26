using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TheAirline.Infrastructure;

namespace TheAirline.Helpers
{
    /// <summary>
    ///     Saves and loads a whole object graph by copying the fields of its objects (what BinaryFormatter used to do).
    ///     Objects are created first and filled afterwards, so references that go in circles (an airline owns an airliner
    ///     that points back to the airline, and so on) come back intact. DataContractSerializer cannot do that for the
    ///     game's ISerializable classes: a reference to an object that is still being built is lost.
    /// </summary>
    public static class GraphSerializer
    {
        private const string Magic = "TAPGRAPH2";

        private enum Tag : byte
        {
            Null = 0,
            Ref = 1,
            String = 2,
            Primitive = 3,
            Enum = 4,
            Struct = 5,
            Culture = 6,
            Decimal = 7,
            DateTime = 8,
            TimeSpan = 9,
            Guid = 10,
            DateTimeOffset = 11
        }

        private enum Kind : byte
        {
            Plain = 0,
            Array = 1,
            List = 2,
            Dictionary = 3,
            Set = 4
        }

        private const BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static FieldInfo[] GetFields(Type type)
        {
            var list = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof (object) && t != typeof (ValueType); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(InstanceFields))
                {
                    if (f.GetCustomAttribute<NonSerializedAttribute>() != null)
                    {
                        continue;
                    }
                    if (typeof (Delegate).IsAssignableFrom(f.FieldType) || f.FieldType.IsPointer)
                    {
                        continue;
                    }
                    list.Add(f);
                }
            }
            return list.ToArray();
        }

        private static string FieldKey(FieldInfo f)
        {
            return f.DeclaringType.FullName + "::" + f.Name;
        }

        private static Kind GetKind(Type t)
        {
            if (t.IsArray)
            {
                return Kind.Array;
            }
            if (t.IsGenericType)
            {
                Type d = t.GetGenericTypeDefinition();
                if (d == typeof (List<>) || d == typeof (ObservableCollection<>) || d == typeof (Collection<>))
                {
                    return Kind.List;
                }
                if (d == typeof (Dictionary<,>) || d == typeof (SortedDictionary<,>) || d == typeof (SortedList<,>))
                {
                    return Kind.Dictionary;
                }
                if (d == typeof (HashSet<>))
                {
                    return Kind.Set;
                }
            }
            return Kind.Plain;
        }

        //values that are never saved: user interface objects, threads, delegates and the like
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> ExcludedCache =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, bool>();

        // the answer only depends on the type, so it is worked out once per type
        private static bool IsExcluded(object value, Type t)
        {
            bool excluded;
            if (ExcludedCache.TryGetValue(t, out excluded)) return excluded;
            excluded = IsExcludedType(value, t);
            ExcludedCache[t] = excluded;
            return excluded;
        }

        private static bool IsExcludedType(object value, Type t)
        {
            if (value is Delegate || value is Type || value is MemberInfo || value is Assembly || value is Module)
            {
                return true;
            }
            if (t == typeof (IntPtr) || t == typeof (UIntPtr))
            {
                return true;
            }
            if (t.IsValueType)
            {
                return false;
            }
            string ns = t.Namespace ?? "";
            return ns.StartsWith("System.Windows", StringComparison.Ordinal) ||
                   ns.StartsWith("System.Threading", StringComparison.Ordinal) ||
                   ns.StartsWith("System.Diagnostics", StringComparison.Ordinal) ||
                   ns.StartsWith("System.ComponentModel.BackgroundWorker", StringComparison.Ordinal) ||
                   t == typeof (System.ComponentModel.BackgroundWorker) ||
                   ns.StartsWith("System.Drawing", StringComparison.Ordinal) ||
                   ns.StartsWith("System.IO", StringComparison.Ordinal) && typeof (Stream).IsAssignableFrom(t);
        }

        private sealed class TypeEntry
        {
            public Type Type;
            public int Index;
            public FieldInfo[] Fields;
        }



        #region Writing

        private sealed class GraphWriter
        {
            private readonly Dictionary<object, int> _ids = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            private readonly List<object> _objects = new List<object>();
            private readonly Dictionary<Type, TypeEntry> _types = new Dictionary<Type, TypeEntry>();
            private readonly List<TypeEntry> _typeList = new List<TypeEntry>();
            private BinaryWriter _w;

            private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();

            public void Write(Stream output, object root)
            {
                GetId(root);

                var payload = new MemoryStream();
                _w = new BinaryWriter(payload);

                // writing an object can discover more objects, which are appended to the list
                for (int i = 0; i < _objects.Count; i++)
                {
                    WriteObject(_objects[i]);
                }
                _w.Flush();

                using (var header = new BinaryWriter(output, System.Text.Encoding.UTF8, true))
                {
                    header.Write(Magic);

                    header.Write(_typeList.Count);
                    foreach (TypeEntry ti in _typeList)
                    {
                        header.Write(ti.Type.AssemblyQualifiedName);
                        if (ti.Fields == null)
                        {
                            header.Write(-1);
                        }
                        else
                        {
                            header.Write(ti.Fields.Length);
                            foreach (FieldInfo f in ti.Fields)
                            {
                                header.Write(FieldKey(f));
                            }
                        }
                    }

                    header.Write(_objects.Count);
                    foreach (object o in _objects)
                    {
                        Type t = o.GetType();
                        header.Write(_types[t].Index);
                        Kind kind = GetKind(t);
                        header.Write((byte) kind);
                        if (kind == Kind.Array)
                        {
                            header.Write(((Array) o).Length);
                        }
                    }

                    header.Write(payload.Length);
                    header.Flush();
                    long walked = _watch.ElapsedMilliseconds;
                    payload.Position = 0;
                    payload.CopyTo(output);
                    if (CollectStats)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("objects " + _objects.Count + ", types " + _typeList.Count + ", payload " + payload.Length/1024 + " KB, walk " + walked + " ms, compress+write " + (_watch.ElapsedMilliseconds - walked) + " ms");
                        foreach (var g in _objects.GroupBy(o => o.GetType()).OrderByDescending(g => g.Count()).Take(25))
                            sb.AppendLine("  " + g.Count().ToString("N0").PadLeft(10) + "  " + g.Key.Name + (g.Key.IsGenericType ? "<" + string.Join(",", g.Key.GetGenericArguments().Select(a => a.Name)) + ">" : ""));
                        LastStats = sb.ToString();
                    }
                }
            }

            private int GetId(object o)
            {
                int id;
                if (!_ids.TryGetValue(o, out id))
                {
                    id = _objects.Count;
                    _ids[o] = id;
                    _objects.Add(o);
                    GetTypeInfo(o.GetType());
                }
                return id;
            }

            private TypeEntry GetTypeInfo(Type t)
            {
                TypeEntry ti;
                if (!_types.TryGetValue(t, out ti))
                {
                    ti = new TypeEntry {Type = t, Index = _typeList.Count};
                    if (!t.IsEnum && !t.IsPrimitive && !t.IsArray && (t.IsValueType || GetKind(t) == Kind.Plain))
                    {
                        ti.Fields = GetFields(t);
                    }
                    _types[t] = ti;
                    _typeList.Add(ti);
                }
                return ti;
            }

            private void WriteObject(object o)
            {
                Type t = o.GetType();
                switch (GetKind(t))
                {
                    case Kind.Array:
                        WriteArray((Array) o, t);
                        break;
                    case Kind.List:
                        {
                            var list = (IList) o;
                            _w.Write(list.Count);
                            foreach (object item in list)
                            {
                                WriteValue(item);
                            }
                        }
                        break;
                    case Kind.Dictionary:
                        {
                            var dict = (IDictionary) o;
                            _w.Write(dict.Count);
                            foreach (DictionaryEntry entry in dict)
                            {
                                WriteValue(entry.Key);
                                WriteValue(entry.Value);
                            }
                        }
                        break;
                    case Kind.Set:
                        {
                            List<object> items = ((IEnumerable) o).Cast<object>().ToList();
                            _w.Write(items.Count);
                            foreach (object item in items)
                            {
                                WriteValue(item);
                            }
                        }
                        break;
                    default:
                        foreach (FieldInfo f in _types[t].Fields)
                        {
                            WriteValue(f.GetValue(o));
                        }
                        break;
                }
            }

            private void WriteArray(Array array, Type t)
            {
                Type element = t.GetElementType();
                if (array.Rank == 1 && element.IsPrimitive && element != typeof (IntPtr) && element != typeof (UIntPtr))
                {
                    int length = Buffer.ByteLength(array);
                    var bytes = new byte[length];
                    Buffer.BlockCopy(array, 0, bytes, 0, length);
                    _w.Write(bytes);
                    return;
                }

                // only one dimensional arrays are used by the game
                if (array.Rank != 1)
                {
                    throw new NotSupportedException("Arrays with " + array.Rank + " dimensions cannot be saved: " + t);
                }
                foreach (object item in array)
                {
                    WriteValue(item);
                }
            }

            private void WriteValue(object v)
            {
                if (v == null)
                {
                    _w.Write((byte) Tag.Null);
                    return;
                }

                var s = v as string;
                if (s != null)
                {
                    _w.Write((byte) Tag.String);
                    _w.Write(s);
                    return;
                }

                var culture = v as CultureInfo;
                if (culture != null)
                {
                    _w.Write((byte) Tag.Culture);
                    _w.Write(culture.Name);
                    return;
                }

                Type t = v.GetType();

                if (IsExcluded(v, t))
                {
                    _w.Write((byte) Tag.Null);
                    return;
                }

                if (t.IsEnum)
                {
                    _w.Write((byte) Tag.Enum);
                    _w.Write(GetTypeInfo(t).Index);
                    Type underlying = Enum.GetUnderlyingType(t);
                    _w.Write(underlying == typeof (ulong)
                                 ? unchecked((long) Convert.ToUInt64(v, CultureInfo.InvariantCulture))
                                 : Convert.ToInt64(v, CultureInfo.InvariantCulture));
                    return;
                }

                if (t.IsPrimitive)
                {
                    _w.Write((byte) Tag.Primitive);
                    TypeCode code = Type.GetTypeCode(t);
                    _w.Write((byte) code);
                    switch (code)
                    {
                        case TypeCode.Boolean: _w.Write((bool) v); break;
                        case TypeCode.Char: _w.Write((ushort) (char) v); break;
                        case TypeCode.SByte: _w.Write((sbyte) v); break;
                        case TypeCode.Byte: _w.Write((byte) v); break;
                        case TypeCode.Int16: _w.Write((short) v); break;
                        case TypeCode.UInt16: _w.Write((ushort) v); break;
                        case TypeCode.Int32: _w.Write((int) v); break;
                        case TypeCode.UInt32: _w.Write((uint) v); break;
                        case TypeCode.Int64: _w.Write((long) v); break;
                        case TypeCode.UInt64: _w.Write((ulong) v); break;
                        case TypeCode.Single: _w.Write((float) v); break;
                        case TypeCode.Double: _w.Write((double) v); break;
                        default: throw new NotSupportedException("Cannot save a value of type " + t);
                    }
                    return;
                }

                if (t == typeof (decimal))
                {
                    _w.Write((byte) Tag.Decimal);
                    _w.Write((decimal) v);
                    return;
                }
                if (t == typeof (DateTime))
                {
                    var d = (DateTime) v;
                    _w.Write((byte) Tag.DateTime);
                    _w.Write(d.Ticks);
                    _w.Write((byte) d.Kind);
                    return;
                }
                if (t == typeof (TimeSpan))
                {
                    _w.Write((byte) Tag.TimeSpan);
                    _w.Write(((TimeSpan) v).Ticks);
                    return;
                }
                if (t == typeof (Guid))
                {
                    _w.Write((byte) Tag.Guid);
                    _w.Write(((Guid) v).ToByteArray());
                    return;
                }
                if (t == typeof (DateTimeOffset))
                {
                    var d = (DateTimeOffset) v;
                    _w.Write((byte) Tag.DateTimeOffset);
                    _w.Write(d.Ticks);
                    _w.Write((long) d.Offset.Ticks);
                    return;
                }

                if (t.IsValueType)
                {
                    _w.Write((byte) Tag.Struct);
                    TypeEntry ti = GetTypeInfo(t);
                    _w.Write(ti.Index);
                    foreach (FieldInfo f in ti.Fields)
                    {
                        WriteValue(f.GetValue(v));
                    }
                    return;
                }

                _w.Write((byte) Tag.Ref);
                _w.Write(GetId(v));
            }
        }

        #endregion

        #region Reading

        private sealed class GraphReader
        {
            private readonly BinaryReader _r;
            private Type[] _types;
            private FieldInfo[][] _fields;
            private object[] _objects;
            private int _failedFields;
            private string _firstFailure;

            public GraphReader(BinaryReader reader)
            {
                _r = reader;
            }

            public object Read()
            {
                if (_r.ReadString() != Magic)
                {
                    throw new InvalidDataException("This is not a save file of this version of the game.");
                }

                int typeCount = _r.ReadInt32();
                _types = new Type[typeCount];
                _fields = new FieldInfo[typeCount][];
                for (int i = 0; i < typeCount; i++)
                {
                    string name = _r.ReadString();
                    Type type = Type.GetType(name, false);
                    _types[i] = type;
                    if (type == null)
                    {
                        StartupLog.Write("Save: type not found any more: " + name);
                    }

                    int fieldCount = _r.ReadInt32();
                    if (fieldCount >= 0)
                    {
                        var keys = new string[fieldCount];
                        for (int k = 0; k < fieldCount; k++)
                        {
                            keys[k] = _r.ReadString();
                        }

                        var fields = new FieldInfo[fieldCount];
                        if (type != null)
                        {
                            Dictionary<string, FieldInfo> known = GetFields(type).GroupBy(FieldKey).ToDictionary(g => g.Key, g => g.First());
                            for (int k = 0; k < fieldCount; k++)
                            {
                                FieldInfo f;
                                fields[k] = known.TryGetValue(keys[k], out f) ? f : null;
                            }
                        }
                        _fields[i] = fields;
                    }
                }

                int objectCount = _r.ReadInt32();
                _objects = new object[objectCount];
                var typeOf = new int[objectCount];
                var kinds = new Kind[objectCount];
                for (int i = 0; i < objectCount; i++)
                {
                    int typeIndex = _r.ReadInt32();
                    var kind = (Kind) _r.ReadByte();
                    int length = kind == Kind.Array ? _r.ReadInt32() : 0;
                    typeOf[i] = typeIndex;
                    kinds[i] = kind;

                    Type type = _types[typeIndex];
                    if (type == null)
                    {
                        _objects[i] = new object();
                        continue;
                    }

                    switch (kind)
                    {
                        case Kind.Array:
                            _objects[i] = Array.CreateInstance(type.GetElementType(), length);
                            break;
                        case Kind.Plain:
                            _objects[i] = RuntimeHelpers.GetUninitializedObject(type);
                            break;
                        default:
                            _objects[i] = Activator.CreateInstance(type, true);
                            break;
                    }
                }

                _r.ReadInt64(); // payload length

                var pendingKeys = new Dictionary<int, List<object>>();
                for (int i = 0; i < objectCount; i++)
                {
                    object o = _objects[i];
                    Type type = _types[typeOf[i]];
                    switch (kinds[i])
                    {
                        case Kind.Array:
                            ReadArray(o, type);
                            break;
                        case Kind.List:
                            {
                                int count = _r.ReadInt32();
                                var list = o as IList;
                                for (int c = 0; c < count; c++)
                                {
                                    object item = ReadValue();
                                    if (list != null)
                                    {
                                        list.Add(item);
                                    }
                                }
                            }
                            break;
                        case Kind.Dictionary:
                        case Kind.Set:
                            {
                                // filled after everything else, because hash codes depend on the fields of the keys
                                int count = _r.ReadInt32();
                                var items = new List<object>(count*2);
                                for (int c = 0; c < count; c++)
                                {
                                    items.Add(ReadValue());
                                    if (kinds[i] == Kind.Dictionary)
                                    {
                                        items.Add(ReadValue());
                                    }
                                }
                                pendingKeys[i] = items;
                            }
                            break;
                        default:
                            {
                                FieldInfo[] fields = _fields[typeOf[i]];
                                foreach (FieldInfo f in fields)
                                {
                                    object value = ReadValue();
                                    SetField(f, o, value);
                                }
                            }
                            break;
                    }
                }

                foreach (KeyValuePair<int, List<object>> pending in pendingKeys)
                {
                    object o = _objects[pending.Key];
                    List<object> items = pending.Value;
                    if (kinds[pending.Key] == Kind.Dictionary)
                    {
                        var dict = o as IDictionary;
                        if (dict == null)
                        {
                            continue;
                        }
                        for (int k = 0; k + 1 < items.Count; k += 2)
                        {
                            if (items[k] != null)
                            {
                                dict[items[k]] = items[k + 1];
                            }
                        }
                    }
                    else
                    {
                        MethodInfo add = o.GetType().GetMethod("Add");
                        if (add == null)
                        {
                            continue;
                        }
                        foreach (object item in items)
                        {
                            add.Invoke(o, new[] {item});
                        }
                    }
                }

                if (_failedFields > 0)
                {
                    StartupLog.Write("Save: " + _failedFields + " values could not be restored, first: " + _firstFailure);
                }

                return _objects.Length > 0 ? _objects[0] : null;
            }

            private void SetField(FieldInfo f, object target, object value)
            {
                if (f == null || target == null)
                {
                    return;
                }
                try
                {
                    f.SetValue(target, value);
                }
                catch (Exception e)
                {
                    _failedFields++;
                    if (_firstFailure == null)
                    {
                        Exception inner = e;
                        while (inner.InnerException != null)
                        {
                            inner = inner.InnerException;
                        }
                        _firstFailure = FieldKey(f) + ": " + inner.GetType().Name + ": " + inner.Message + " " + inner.StackTrace;
                    }
                }
            }

            private void ReadArray(object o, Type type)
            {
                var array = o as Array;
                if (array == null)
                {
                    return;
                }

                Type element = type.GetElementType();
                if (element.IsPrimitive && element != typeof (IntPtr) && element != typeof (UIntPtr))
                {
                    int length = Buffer.ByteLength(array);
                    byte[] bytes = _r.ReadBytes(length);
                    Buffer.BlockCopy(bytes, 0, array, 0, length);
                    return;
                }

                for (int i = 0; i < array.Length; i++)
                {
                    object value = ReadValue();
                    try
                    {
                        array.SetValue(value, i);
                    }
                    catch (Exception e)
                    {
                        _failedFields++;
                        if (_firstFailure == null)
                        {
                            _firstFailure = "array of " + element + ": " + e.Message;
                        }
                    }
                }
            }

            private object ReadValue()
            {
                var tag = (Tag) _r.ReadByte();
                switch (tag)
                {
                    case Tag.Null:
                        return null;
                    case Tag.Ref:
                        return _objects[_r.ReadInt32()];
                    case Tag.String:
                        return _r.ReadString();
                    case Tag.Culture:
                        return CultureInfo.GetCultureInfo(_r.ReadString());
                    case Tag.Enum:
                        {
                            Type t = _types[_r.ReadInt32()];
                            long value = _r.ReadInt64();
                            return t == null ? null : Enum.ToObject(t, Enum.GetUnderlyingType(t) == typeof (ulong) ? (object) unchecked((ulong) value) : value);
                        }
                    case Tag.Primitive:
                        {
                            var code = (TypeCode) _r.ReadByte();
                            switch (code)
                            {
                                case TypeCode.Boolean: return _r.ReadBoolean();
                                case TypeCode.Char: return (char) _r.ReadUInt16();
                                case TypeCode.SByte: return _r.ReadSByte();
                                case TypeCode.Byte: return _r.ReadByte();
                                case TypeCode.Int16: return _r.ReadInt16();
                                case TypeCode.UInt16: return _r.ReadUInt16();
                                case TypeCode.Int32: return _r.ReadInt32();
                                case TypeCode.UInt32: return _r.ReadUInt32();
                                case TypeCode.Int64: return _r.ReadInt64();
                                case TypeCode.UInt64: return _r.ReadUInt64();
                                case TypeCode.Single: return _r.ReadSingle();
                                case TypeCode.Double: return _r.ReadDouble();
                                default: throw new InvalidDataException("Unknown primitive type " + code);
                            }
                        }
                    case Tag.Decimal:
                        return _r.ReadDecimal();
                    case Tag.DateTime:
                        {
                            long ticks = _r.ReadInt64();
                            var kind = (DateTimeKind) _r.ReadByte();
                            return new DateTime(ticks, kind);
                        }
                    case Tag.TimeSpan:
                        return new TimeSpan(_r.ReadInt64());
                    case Tag.Guid:
                        return new Guid(_r.ReadBytes(16));
                    case Tag.DateTimeOffset:
                        {
                            long ticks = _r.ReadInt64();
                            long offset = _r.ReadInt64();
                            return new DateTimeOffset(ticks, new TimeSpan(offset));
                        }
                    case Tag.Struct:
                        {
                            int index = _r.ReadInt32();
                            Type t = _types[index];
                            FieldInfo[] fields = _fields[index];
                            object boxed = t == null ? null : RuntimeHelpers.GetUninitializedObject(t);
                            foreach (FieldInfo f in fields)
                            {
                                object value = ReadValue();
                                SetField(f, boxed, value);
                            }
                            return boxed;
                        }
                    default:
                        throw new InvalidDataException("Unknown value tag " + tag);
                }
            }
        }

        #endregion

        #region Public Methods and Operators

        /// <summary>Set to get a breakdown of the last save in LastStats (for finding what makes saving slow).</summary>
        public static bool CollectStats;
        public static string LastStats;

        public static void Save(string filename, object root)
        {
            using (var file = File.Open(filename, FileMode.Create))
            using (var zip = new GZipStream(file, CompressionLevel.Fastest))
            {
                new GraphWriter().Write(zip, root);
            }
        }

        public static T Load<T>(string filename)
        {
            using (var file = File.Open(filename, FileMode.Open, FileAccess.Read))
            using (var zip = new GZipStream(file, CompressionMode.Decompress))
            using (var buffered = new BufferedStream(zip, 1 << 20))
            using (var reader = new BinaryReader(buffered))
            {
                return (T) new GraphReader(reader).Read();
            }
        }

        #endregion
    }
}
