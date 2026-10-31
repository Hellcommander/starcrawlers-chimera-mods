using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SCClassLoader
{
    /// <summary>
    /// XML mod files: anything a json file can hold (a class, a library, data such as synergies or items) can be
    /// written as XML instead. The file is turned into the same json tree, so every field works the same way.
    ///   - attributes and child elements are interchangeable: &lt;action do="damage" min="2"/&gt;
    ///   - lists are wrapper elements with one child per entry: &lt;actions&gt;&lt;action .../&gt;&lt;action .../&gt;&lt;/actions&gt;
    ///     (the child's name doesn't matter), or a single entry written directly;
    ///   - lists of words may be a comma list: trees="SKILLTREE_Pyromaniac,SKILLTREE_Chemistry";
    ///   - several classes in one file: a root holding &lt;class&gt; elements.
    /// </summary>
    internal static class XmlMods
    {
        /// <summary>Field name -> element type for every list field in the mod format (found by reflection).</summary>
        private static Dictionary<string, Type> lists;

        private static readonly string[] DataLists = { "items", "tweaks", "enemies", "recipes", "factions", "synergies", "effects", "tiers" };

        internal static JToken Load(string file)
        {
            var doc = new XmlDocument();
            doc.Load(file);
            XmlElement root = doc.DocumentElement;
            if (root == null) throw new InvalidDataException("empty XML file");
            JToken tok = JToken.Parse(JsonConvert.SerializeXmlNode(root, Newtonsoft.Json.Formatting.None, omitRootObject: true));
            tok = Clean(tok, null);
            if (!(tok is JObject obj)) throw new InvalidDataException("the root element must hold fields");
            // a root holding <class> elements: several classes in one file
            if (obj["tiers"] == null && obj["class"] != null)
            {
                var arr = new JArray();
                foreach (JToken c in obj["class"] is JArray a ? (IEnumerable<JToken>)a : new[] { obj["class"] })
                    arr.Add(Normalize(c, typeof(ClassDef)));
                obj.Remove("class");
                if (obj.Properties().Any()) arr.Add(Normalize(obj, null)); // data or a library next to them
                return arr;
            }
            return Normalize(obj, obj["tiers"] != null ? typeof(ClassDef) : null);
        }

        /// <summary>Strips the converter's markup: "@attr" keys, "#text" values, xmlns noise.</summary>
        private static JToken Clean(JToken t, string name)
        {
            if (t is JArray a) return new JArray(a.Select(x => Clean(x, name)));
            if (!(t is JObject o)) return t;
            if (o.Count == 1 && o["#text"] != null) return o["#text"];
            var r = new JObject();
            foreach (JProperty p in o.Properties())
            {
                if (p.Name.StartsWith("@xmlns") || p.Name == "?xml" || p.Name == "#comment") continue;
                string key = p.Name.StartsWith("@") ? p.Name.Substring(1) : p.Name;
                r[key] = Clean(p.Value, key);
            }
            return r;
        }

        /// <summary>Turns list fields into arrays (unwrapping their container element) all the way down.</summary>
        private static JToken Normalize(JToken t, Type hint)
        {
            if (t is JArray arr) return new JArray(arr.Select(x => Normalize(x, hint)));
            if (!(t is JObject o)) return t;
            var lf = Lists();
            foreach (JProperty p in o.Properties().ToList())
            {
                Type elem = null;
                bool isList = (hint != null && ListElem(hint, p.Name, out elem)) || lf.TryGetValue(p.Name, out elem) || DataLists.Contains(p.Name);
                JToken v = p.Value;
                if (isList)
                {
                    v = Unwrap(v);
                    if (v.Type == JTokenType.Null) { p.Value = v; continue; }
                    if (v.Type == JTokenType.String && (elem == null || elem == typeof(string)) && ((string)v).Contains(","))
                        v = new JArray(((string)v).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
                    if (!(v is JArray)) v = new JArray(v);
                    p.Value = new JArray(((JArray)v).Select(x => Normalize(x, elem)));
                }
                else p.Value = Normalize(v, FieldType(hint, p.Name));
            }
            return o;
        }

        /// <summary>&lt;actions&gt;&lt;action/&gt;&lt;action/&gt;&lt;/actions&gt; arrives as {"action": [...]}: take the inside.</summary>
        private static JToken Unwrap(JToken v)
        {
            if (v is JObject o && o.Count == 1)
            {
                JToken inner = o.Properties().First().Value;
                if (inner is JArray || inner is JObject || inner.Type == JTokenType.String) return inner;
            }
            return v;
        }

        private static Type FieldType(Type t, string name)
        {
            if (t == null) return null;
            MemberInfo m = t.GetField(name) ?? (MemberInfo)t.GetProperty(name);
            Type ft = (m as FieldInfo)?.FieldType ?? (m as PropertyInfo)?.PropertyType;
            if (ft == null) return null;
            Type under = Nullable.GetUnderlyingType(ft) ?? ft;
            return under.IsClass && under != typeof(string) ? under : null;
        }

        private static bool ListElem(Type t, string name, out Type elem)
        {
            elem = null;
            MemberInfo m = t.GetField(name) ?? (MemberInfo)t.GetProperty(name);
            Type ft = (m as FieldInfo)?.FieldType ?? (m as PropertyInfo)?.PropertyType;
            return ft != null && IsListType(ft, out elem);
        }

        private static bool IsListType(Type ft, out Type elem)
        {
            elem = null;
            if (ft == typeof(string)) return false;
            if (ft.IsArray) { elem = ft.GetElementType(); return true; }
            if (ft.IsGenericType && typeof(IList).IsAssignableFrom(ft)) { elem = ft.GetGenericArguments()[0]; return true; }
            return false;
        }

        private static Dictionary<string, Type> Lists()
        {
            if (lists != null) return lists;
            lists = new Dictionary<string, Type>();
            foreach (Type t in typeof(XmlMods).Assembly.GetTypes().Where(x => x.IsPublic && x.IsClass && x.Namespace == typeof(XmlMods).Namespace))
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (IsListType(f.FieldType, out Type e) && !lists.ContainsKey(f.Name)) lists[f.Name] = e;
            return lists;
        }
    }
}
