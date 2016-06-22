#define GENERATE_DEMO_DATA

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Utils
{
	public sealed class CsvIgnoreAttribute : Attribute
	{
	}

	public static class CSV
	{
		private enum TypeMode
		{
			SimpleSingle = 0,
			SimpleMany = 1,
			ComplexSingle = 2,
			ComplexMany = 3,
			NonSuported = 5
		}

		private class TypeMap
		{
			public ValueMember GetComplexMember(Type mainType, string name)
			{
				TypeData typeData = ComplexTypeDatas[mainType];
				foreach (var mem in typeData.Singles)
				{
					if (mem.Name == name)
					{
						return mem;
					}
				}
				foreach (var mem in typeData.Many)
				{
					if (mem.Name == name)
					{
						return mem;
					}
				}
				return null;
			}

			public readonly List<ValueMember> SimpleSingle
				= new List<ValueMember>();

			public readonly Dictionary<ValueMember, Type> SimpleMany
				= new Dictionary<ValueMember, Type>();

			public readonly Dictionary<Type, TypeData> ComplexTypeDatas
				= new Dictionary<Type, TypeData>();

			public class TypeData
			{
				public readonly List<ValueMember> Singles
					= new List<ValueMember>();

				public readonly List<ValueMember> Many
					= new List<ValueMember>();
			}
		}

		private class ValueMember
		{
			private readonly PropertyInfo _property;
			private readonly FieldInfo _field;

			public ValueMember(PropertyInfo prop, FieldInfo field)
			{
				_property = prop;
				_field = field;
			}

			public void SetValue<T>(ref T obj, object value)
			{
				object boxed = obj;
				if (_property != null)
				{
					_property.SetValue(boxed, value, null);
				}
				else
				{
					_field.SetValue(boxed, value);
				}
				obj = (T) boxed;
			}

			public object GetValue(object obj)
			{
				if (_property != null)
				{
					return _property.GetValue(obj, null);
				}
				return _field.GetValue(obj);
			}

			public Type MemberType
			{
				get
				{
					if (_property != null)
					{
						return _property.PropertyType;
					}
					return _field.FieldType;
				}
			}

			public string Name
			{
				get
				{
					if (_property != null)
					{
						return _property.Name;
					}
					return _field.Name;
				}
			}
		}

		private static string NormalizedTypeName(Type type)
		{
			if (type.IsGenericType)
			{
				var genType = type.GetGenericTypeDefinition();
				if (genType == typeof (KeyValuePair<,>))
				{
					Type[] arg = type.GetGenericArguments();
					return "KV<" + arg[0].Name + "," + arg[1].Name + ">";
				}
				Error("Generic type not supported in context: " + type.Name);
			}
			return type.Name;
		}

		private static readonly Dictionary<Type, TypeMap> _cashedTypeMaps = new Dictionary<Type, TypeMap>();

		private static void AddType(this Dictionary<Type, TypeMap.TypeData> dic, Type type)
		{
			if (!dic.ContainsKey(type))
			{
				TypeMap.TypeData tData = new TypeMap.TypeData();
				dic.Add(type, tData);
			}
		}

		private static StringBuilder GetSimpleHeader(Type type)
		{
			StringBuilder sb = new StringBuilder();
			List<ValueMember> properties = GetTypeMap(type).SimpleSingle;
			bool first = true;
			foreach (var p in properties)
			{
				CheckNonFirst(ref first, sb, "\t");
				sb.Append(p.Name);
			}
			if (sb.Length > 0)
			{
				return sb;
			}
			sb.Append(_SPEC + _SPEC);
			return sb;
		}

		private static TypeMode GetTypeMode(Type pType, out Type nestedType)
		{
			nestedType = pType;
			if (_simpleTypes.Contains(pType) || pType.IsEnum)
			{
				return TypeMode.SimpleSingle;
			}
			if (pType.IsArray)
			{
				var elementType = pType.GetElementType();
				if (elementType == null)
				{
					Error("Element type is null");
					return TypeMode.NonSuported;
				}
				nestedType = elementType;
				if (_simpleTypes.Contains(elementType) || elementType.IsEnum)
				{
					return TypeMode.SimpleMany;
				}
				if (CheckForStructedClass(elementType))
				{
					return TypeMode.ComplexMany;
				}
				return TypeMode.NonSuported;
			}
			if (pType.IsGenericType)
			{
				Type gType = pType.GetGenericTypeDefinition();
				if (gType == typeof (List<>))
				{
					var genericArgument = pType.GetGenericArguments()[0];
					nestedType = genericArgument;
					if (_simpleTypes.Contains(genericArgument) || genericArgument.IsEnum)
					{
						return TypeMode.SimpleMany;
					}
					if (CheckForStructedClass(genericArgument))
					{
						return TypeMode.ComplexMany;
					}
					return TypeMode.NonSuported;
				}
				if (gType == typeof (Dictionary<,>))
				{
					nestedType = typeof (KeyValuePair<,>).MakeGenericType(pType.GetGenericArguments());
					return TypeMode.ComplexMany;
				}
				Error("Non supported generic type " + pType.Name);
				return TypeMode.NonSuported;
			}
			if (pType.IsPrimitive)
			{
				Error("Not supported primitive type " + pType.Name);
				return TypeMode.NonSuported;
			}
			if (pType.IsClass || pType.IsValueType)
			{
				return TypeMode.ComplexSingle;
			}
			Error("Not supported type " + pType.Name);
			return TypeMode.NonSuported;
		}

		private static Type GetTypeFromValueMemberName(object obj, string name)
		{
			PropertyInfo pInfo = obj.GetType().GetProperty(name);
			if (pInfo != null)
			{
				return pInfo.PropertyType;
			}
			return obj.GetType().GetField(name).FieldType;
		}

		private class PropertyAndFields
		{
			public string[] FieldNames { get; private set; }
			public string[] PropertyNames { get; private set; }

			public PropertyAndFields(string[] fieldNames, string[] propertyNames)
			{
				if (fieldNames == null)
				{
					fieldNames = new string[0];
				}
				if (propertyNames == null)
				{
					propertyNames = new string[0];
				}
				FieldNames = fieldNames;
				PropertyNames = propertyNames;
			}
		}

		private static readonly Dictionary<Type, PropertyAndFields> _specTypesList = new Dictionary<Type, PropertyAndFields>
		{
			{typeof (Vector3), new PropertyAndFields(new[] {"x", "y", "z"}, null)},
			{typeof (KeyValuePair<,>), new PropertyAndFields(null, new[] {"Key", "Value"})}
		};

		private static TypeMap GetTypeMap(Type type)
		{
			if (_cashedTypeMaps.ContainsKey(type))
			{
				return _cashedTypeMaps[type];
			}

			TypeMap typeMap = new TypeMap();

			List<ValueMember> members = new List<ValueMember>();
			if (!_specTypesList.ContainsKey(type))
			{
				PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
				IEnumerable<PropertyInfo> nonIgnoredProperties
					= properties.Where(CheckNonIgnoreAttribute);
				foreach (var m in nonIgnoredProperties)
				{
					members.Add(new ValueMember(m, null));
				}

				FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
				IEnumerable<FieldInfo> nonIgnoredFields
					= fields.Where(CheckNonIgnoreAttribute);
				foreach (var m in nonIgnoredFields)
				{
					members.Add(new ValueMember(null, m));
				}
			}
			else
			{
				PropertyAndFields pAndF = _specTypesList[type];
				foreach (var pName in pAndF.PropertyNames)
				{
					members.Add(new ValueMember(type.GetProperty(pName), null));
				}
				foreach (var fName in pAndF.FieldNames)
				{
					members.Add(new ValueMember(null, type.GetField(fName)));
				}
			}

			foreach (var p in members)
			{
				var pType = p.MemberType;
				Type targetType;
				TypeMode typeMode = GetTypeMode(pType, out targetType);
				switch (typeMode)
				{
					case TypeMode.SimpleSingle:
						typeMap.SimpleSingle.Add(p);
						break;
					case TypeMode.SimpleMany:
						typeMap.SimpleMany.Add(p, targetType);
						break;
					case TypeMode.ComplexSingle:
						typeMap.ComplexTypeDatas.AddType(pType);
						typeMap.ComplexTypeDatas[pType].Singles.Add(p);
						break;
					case TypeMode.ComplexMany:
						typeMap.ComplexTypeDatas.AddType(targetType);
						typeMap.ComplexTypeDatas[targetType].Many.Add(p);
						break;
					case TypeMode.NonSuported:
						break;
				}
			}
			_cashedTypeMaps.Add(type, typeMap);
			return typeMap;
		}

		private const string _SPEC = "¶";

		private static readonly List<Type> _simpleTypes = new List<Type>
		{
			typeof (int),
			typeof (float),
			typeof (string),
			typeof (bool)
		};

		public static string Serialize(object obj)
		{
			try
			{
				StringBuilder sb = new StringBuilder();
				Type nested;
				Type type = obj.GetType();
				var mode = GetTypeMode(type, out nested);
				switch (mode)
				{
					case TypeMode.ComplexSingle:
						sb.Append(GetSimpleHeader(type));
						sb.Append("\n");
						sb.Append(GenerateObjectBody(obj, type));
						break;

					case TypeMode.ComplexMany:
						sb.Append(GetSimpleHeader(nested));
						sb.Append("\n");
						int count;
						sb.Append(GetComplexArrayList(out count, obj as IEnumerable, nested));
						break;
					default:
						Error("This type as ROOT not implemented: " + type.Name);
						break;
				}
				return sb.ToString();
			}
			catch (Exception ex)
			{
				Debug.LogError("Object not serialize. Returned null\n"
					+ ex.Message + "\n" + ex.StackTrace);
				return null;
			}
		}

		private static StringBuilder GenerateObjectBody(object obj, Type type)
		{
			StringBuilder sb = new StringBuilder();

			sb.Append(GetSimplePart(type, obj));

			string simpleManyPart = GetSimpleManyPart(type, obj).ToString();
			if (!String.IsNullOrEmpty(simpleManyPart))
			{
				sb.Append("\n");
				sb.Append(simpleManyPart);
			}

			string complexPart = GetComplexPart(type, obj).ToString();
			if (!String.IsNullOrEmpty(complexPart))
			{
				sb.Append("\n");
				sb.Append(complexPart);
			}

			return sb;
		}

		private static void CheckNonFirst(ref bool isFirst, StringBuilder sb, string str)
		{
			if (isFirst)
			{
				isFirst = false;
			}
			else
			{
				sb.Append(str);
			}
		}

		private static StringBuilder GetSimplePart(Type type, object obj)
		{
			StringBuilder sb = new StringBuilder();
			bool first = true;
			if (GetTypeMap(type).SimpleSingle.Count == 0)
			{
				sb.Append(_SPEC + _SPEC);
				return sb;
			}
			foreach (var p in GetTypeMap(type).SimpleSingle)
			{
				CheckNonFirst(ref first, sb, "\t");
				var o = p.GetValue(obj);
				o = NormalizeSimpleValue(o, p.MemberType);
				sb.Append(o);
			}

			return sb;
		}

		private static string NormalizeSimpleValue(object obj, Type type)
		{
			if (type.IsEnum || type == typeof (int))
			{
				return (Convert.ToInt32(obj, CultureInfo.InvariantCulture)).
					ToString(CultureInfo.InvariantCulture);
			}
			if (type == typeof (string))
			{
				string str = (string) obj ?? "";
				str = str.Replace("\\", "\\\\");
				str = str.Replace("\n", "\\n");
				str = str.Replace("\t", "\\t");
				return "\"" + str + "\"";
			}
			if (type == typeof (float))
			{
				return Convert.ToSingle(obj, CultureInfo.InvariantCulture).
					ToString(CultureInfo.InvariantCulture);
			}
			if (type == typeof (bool))
			{
				var value = Convert.ToBoolean(obj, CultureInfo.InvariantCulture);
				return value ? "1" : "0";
			}

			Error("Not supported normalize for simple type " + type.Name);
			return obj.ToString();
		}

		private static StringBuilder GetSimpleManyPart(Type type, object obj)
		{
			StringBuilder sb = new StringBuilder();
			bool firstType = true;

			foreach (var p in GetTypeMap(type).SimpleMany)
			{
				IEnumerable arr = p.Key.GetValue(obj) as IEnumerable;
#if GENERATE_DEMO_DATA
				if (arr == null)
				{
					arr = new object[5];
				}
#endif
				if (arr != null)
				{
					CheckNonFirst(ref firstType, sb, "\n");
					sb.Append(p.Key.Name + ":");
					AddShifted(sb, GetSimpleArrayList(arr, p.Value).ToString());
				}
			}
			return sb;
		}

		private static void AddShifted(StringBuilder sb, string str)
		{
			if (!String.IsNullOrEmpty(str))
			{
				sb.Append("\t" + str.Replace("\n", "\n\t"));
			}
		}

		private static StringBuilder GetComplexPart(Type type, object obj)
		{
			StringBuilder sb = new StringBuilder();

			bool firstType = true;
			foreach (KeyValuePair<Type, TypeMap.TypeData> data in GetTypeMap(type).ComplexTypeDatas)
			{
				bool firstObj = true;
				foreach (ValueMember p in data.Value.Singles)
				{
					object o = p.GetValue(obj);
#if GENERATE_DEMO_DATA
					if (o == null)
					{
						o = Activator.CreateInstance(p.MemberType);
					}
#endif
					if (o == null)
					{
						continue;
					}

					if (firstObj)
					{
						CheckNonFirst(ref firstType, sb, "\n");
						sb.Append(_SPEC + p.MemberType.Name);
						string header = GetSimpleHeader(p.MemberType).ToString();
						AddShifted(sb, header);
						firstObj = false;
					}

					string[] str = GenerateObjectBody(o, p.MemberType).ToString().Split('\n');
					for (int index = 0; index < str.Length; index++)
					{
						sb.Append("\n");
						if (index == 0)
						{
							sb.Append(p.Name + ":");
						}
						if (str[index].Length > 0)
						{
							sb.Append("\t" + str[index]);
						}
					}
				}
				foreach (ValueMember p in data.Value.Many)
				{
					IEnumerable arr = p.GetValue(obj) as IEnumerable;
#if GENERATE_DEMO_DATA
					if (arr == null)
					{
						arr = new object[0];
					}
#endif

					if (arr == null)
					{
						continue;
					}

					if (firstObj)
					{
						CheckNonFirst(ref firstType, sb, "\n");
						sb.Append(_SPEC + NormalizedTypeName(data.Key));
						string header = GetSimpleHeader(data.Key).ToString();
						if (!String.IsNullOrEmpty(header))
						{
							sb.Append("\t" + header);
						}
						firstObj = false;
					}

					int count;
					string[] str = GetComplexArrayList(out count, arr, data.Key).ToString().Split('\n');
					for (int index = 0; index < str.Length; index++)
					{
						sb.Append("\n");
						if (index == 0)
						{
							sb.Append(p.Name + ":");
						}
						if (count > 0)
						{
							sb.Append("\t" + str[index]);
						}
					}
				}
			}
			return sb;
		}

		private static StringBuilder GetSimpleArrayList(IEnumerable arr, Type elementType)
		{
			StringBuilder sb = new StringBuilder();
			int i = 0;
			foreach (object obj in arr)
			{
				if (i != 0)
				{
					if (i%5 == 0)
					{
						sb.Append("\n");
					}
					else
					{
						sb.Append("\t");
					}
				}
				i++;

				object o = NormalizeSimpleValue(obj, elementType);
				sb.Append(o);
			}
			return sb;
		}

		private static StringBuilder GetComplexArrayList(out int count, IEnumerable arr, Type elementType)
		{
			count = 0;
			StringBuilder sb = new StringBuilder();
			bool first = true;
			foreach (object o in arr)
			{
				count++;
				CheckNonFirst(ref first, sb, "\n");
				if (o == null)
				{
					sb.Append(_SPEC);
				}
				else
				{
					sb.Append(GenerateObjectBody(o, elementType));
				}
			}
			return sb;
		}

		private static bool CheckForStructedClass(Type type)
		{
			if (type.IsArray)
			{
				Error("Nested array not supported");
				return false;
			}
			if (type.IsGenericType)
			{
				Error("Nested generic not supported");
				return false;
			}
			if (type.IsPrimitive)
			{
				Error("Not supported primitive type " + type.Name);
				return false;
			}
			if (type.IsClass || type.IsValueType)
			{
				return true;
			}
			Error("Not supported type " + type.Name);
			return false;
		}

		private static bool CheckNonIgnoreAttribute(MemberInfo member)
		{
			return member.GetCustomAttributes(typeof (CsvIgnoreAttribute), true).Length == 0;
		}

		public static T Parse<T>(string str)
		{
			try
			{
				str = str.Replace("\r", "");
				Type nested;
				T output = default(T);
				Type type = typeof (T);
				var mode = GetTypeMode(type, out nested);
				switch (mode)
				{
					case TypeMode.ComplexSingle:
						var m = str.Split('\n');
						List<string> k = new List<string>();
						for (int index = 1; index < m.Length; index++)
						{
							k.Add(m[index]);
						}
						output = (T) ParseSingleObject(type, m[0], k);
						break;

					default:
						Error("This type not implemented, return default value");
						break;
				}
				return output;
			}
			catch (Exception ex)
			{
				Debug.LogError("String not parse. Return default value\n"
					+ ex.Message + "\n" + ex.StackTrace);
				return default(T);
			}
		}

		private static object ParseSingleObject(Type type, string header, List<string> body)
		{
			var obj = ParseSimplePart(type, header, body[0]);

			List<string> bodyArrayes = new List<string>();
			List<string> bodyComplex = new List<string>();
			int index = 1;
			for (; index < body.Count; index++)
			{
				var str = body[index];
				if (!str.StartsWith(_SPEC))
				{
					bodyArrayes.Add(str);
					continue;
				}
				break;
			}

			for (; index < body.Count; index++)
			{
				bodyComplex.Add(body[index]);
			}

			if (bodyArrayes.Count > 0)
			{
				ParseSimpleManyPart(type, ref obj, bodyArrayes);
			}
			if (bodyComplex.Count > 0)
			{
				ParseComplexPart(type, ref obj, bodyComplex);
			}

			return obj;
		}

		private static object ParseSimplePart(Type type, string header, string simplePart)
		{
			if (simplePart == _SPEC)
			{
				return null;
			}

			object obj = Activator.CreateInstance(type);
			if (simplePart == _SPEC + _SPEC)
			{
				return obj;
			}
			var names = header.Split('\t');
			var values = simplePart.Split('\t');
			var map = GetTypeMap(type);
			for (int i = 0; i < names.Length; i++)
			{
				var name = names[i];
				bool finded = false;
				foreach (var valMember in map.SimpleSingle)
				{
					if (name == valMember.Name)
					{
						valMember.SetValue(ref obj,
							StringToObject(values[i], valMember.MemberType));
						finded = true;
						break;
					}
				}
				if (!finded)
				{
					Warning("Field or Property " + name + " not found");
				}
			}
			return obj;
		}

		private static void ParseSimpleManyPart(Type type, ref object obj, List<string> bodyArrayes)
		{
			TypeMap map = GetTypeMap(type);
			for (int i = 0; i < bodyArrayes.Count;)
			{
				string line = bodyArrayes[i];
				string[] lineSplit = line.Split('\t');

				// find ValueMember
				KeyValuePair<ValueMember, Type> k = new KeyValuePair<ValueMember, Type>();
				bool finded = false;
				var arrayName = lineSplit[0].Substring(0, lineSplit[0].Length - 1);
				foreach (var valMember in map.SimpleMany)
				{
					if (arrayName == valMember.Key.Name)
					{
						k = valMember;
						finded = true;
						break;
					}
				}

				// fill list values
				// ReSharper disable once PossibleNullReferenceException
				IList list = (IList) typeof (List<>)
					.MakeGenericType(k.Value)
					.GetConstructor(Type.EmptyTypes)
					.Invoke(null);
				int j = i;
				for (; j < bodyArrayes.Count; j++)
				{
					string str2 = bodyArrayes[j];
					string[] strSplit2 = str2.Split('\t');
					int countOnRow = strSplit2.Length - 1;
					if (j != i && strSplit2[0].EndsWith(":"))
					{
						break;
					}
					for (int l = 1; l <= countOnRow; l++)
					{
						list.Add(StringToObject(strSplit2[l], k.Value));
					}
				}
				i = j;

				if (!finded)
				{
					Warning("Array " + arrayName + " not finded");
					continue;
				}

				if (k.Key.MemberType.IsArray)
				{
					Array y = Array.CreateInstance(k.Value, list.Count);
					list.CopyTo(y, 0);
					k.Key.SetValue(ref obj, y);
				}
				else
				{
					k.Key.SetValue(ref obj, list);
				}
			}
		}

		private static void ParseComplexPart(Type type, ref object obj, List<string> bodyComplex)
		{
			List<string> typeBody = new List<string>();
			for (int i = 0; i < bodyComplex.Count;)
			{
				typeBody.Clear();
				string nameValueMember = bodyComplex[i + 1].Split('\t')[0].Replace(":", "");
				Type memberType = GetTypeFromValueMemberName(obj, nameValueMember);
				Type nestedType;
				GetTypeMode(memberType, out nestedType);

				int k = i;
				for (; k < bodyComplex.Count; k++)
				{
					if (k != i && CheckForTypeName(bodyComplex[k]))
					{
						break;
					}
					typeBody.Add(bodyComplex[k]);
				}
				i = k;
				ParseOneType(type, ref obj, nestedType, typeBody);
			}
		}

		private static void ParseOneType(Type type, ref object obj, Type targetType, List<string> typeBody)
		{
			string header = GetUnshiftedOneLine(typeBody[0]);
			for (int i = 1; i < typeBody.Count;)
			{
				List<string> stringList = new List<string>();
				string memberName = CheckStrForMemberName(typeBody[i]);
				int j = i;
				for (; j < typeBody.Count; j++)
				{
					if (j != i && CheckStrForMemberName(typeBody[j]) != null)
					{
						break;
					}
					stringList.Add(typeBody[j]);
				}
				i = j;

				ValueMember member = GetTypeMap(type).GetComplexMember(targetType, memberName);
				if (member == null)
				{
					Warning("Member " + memberName + " not finded");
					continue;
				}

				UnShiftedBlock(stringList);
				if (member.MemberType.IsGenericType || member.MemberType.IsArray)
				{
					ParseManyObjects(ref obj, member, targetType, header, stringList);
				}
				else
				{
					object memberObj = ParseSingleObject(targetType, header, stringList);
					member.SetValue(ref obj, memberObj);
				}
			}
		}

		private static void ParseManyObjects(ref object obj, ValueMember member, Type targetType, string header, List<string> bodyObjects)
		{
			IList list = (IList) typeof (List<>)
				.MakeGenericType(targetType)
				.GetConstructor(Type.EmptyTypes)
				.Invoke(null);

			int prev = 0;
			List<string> body = new List<string>();
			for (int i = 0; i < bodyObjects.Count;)
			{
				string upperLeftCorner = bodyObjects[i].Split('\t')[0];
				if (upperLeftCorner == "" || upperLeftCorner.EndsWith(":") || i == prev)
				{
					body.Add(bodyObjects[i]);
					i++;
					bool lastRow = !(i < bodyObjects.Count);
					if (!lastRow)
					{
						continue;
					}
				}

				object oneObj = ParseSingleObject(targetType, header, body);
				body.Clear();
				list.Add(oneObj);
				prev = i;
			}

			Type mType = member.MemberType;
			if (mType.IsArray)
			{
				Array y = Array.CreateInstance(targetType, list.Count);
				list.CopyTo(y, 0);
				member.SetValue(ref obj, y);
			}
			else
			{
				Type gType = mType.GetGenericTypeDefinition();
				if (gType == typeof (List<>))
				{
					member.SetValue(ref obj, list);
				}
				if (gType == typeof (Dictionary<,>))
				{
					IDictionary dic = (IDictionary) typeof (Dictionary<,>)
						.MakeGenericType(targetType.GetGenericArguments())
						.GetConstructor(Type.EmptyTypes)
						.Invoke(null);
					var keyProp = targetType.GetProperty("Key");
					var valueProp = targetType.GetProperty("Value");
					foreach (object item in list)
					{
						object key = keyProp.GetValue(item, null);
						object value = valueProp.GetValue(item, null);
						dic.Add(key, value);
					}
					member.SetValue(ref obj, dic);
				}
			}
		}

		private static bool CheckForTypeName(string input)
		{
			var str = input.Split('\t')[0];
			if (str == _SPEC)
			{
				return false;
			}
			if (str == _SPEC + _SPEC)
			{
				return false;
			}
			return str.StartsWith(_SPEC);
		}

		private static string CheckStrForMemberName(string input)
		{
			var m = input.Split('\t');
			if (m[0].EndsWith(":"))
			{
				return m[0].Replace(":", "");
			}
			return null;
		}

		private static void UnShiftedBlock(List<string> block)
		{
			for (int i = 0; i < block.Count; i++)
			{
				block[i] = GetUnshiftedOneLine(block[i]);
			}
		}

		private static string GetUnshiftedOneLine(string input)
		{
			var index = input.IndexOf('\t');
			if (index >= 0)
			{
				input = input.Substring(index + 1);
			}
			return input;
		}

		private static object StringToObject(string str, Type type)
		{
			if (type.IsEnum)
			{
				var num = Convert.ToInt32(str, CultureInfo.InvariantCulture);
				return Enum.ToObject(type, num);
			}
			if (type == typeof (int))
			{
				return Convert.ToInt32(str, CultureInfo.InvariantCulture);
			}
			if (type == typeof (string))
			{
				str = str.Substring(1, str.Length - 2);
				str = str.Replace("\\\\", "\\");
				str = str.Replace("\\n", "\n");
				str = str.Replace("\\t", "\t");
				return str;
			}
			if (type == typeof (float))
			{
				return Convert.ToSingle(str, CultureInfo.InvariantCulture);
			}
			if (type == typeof (bool))
			{
				return str == "1";
			}

			Error("Not supported normalize for simple type " + type.Name);
			return str;
		}

		private static void Error(string msg)
		{
			throw new Exception(msg);
		}

		private static void Warning(string msg)
		{
			Debug.LogWarning(msg);
		}
	}
}