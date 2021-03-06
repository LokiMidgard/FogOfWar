﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Zelos.Scribe
{
    class Serelizer
    {

        private readonly Dictionary<Type, (Func<object, string> Writer, Func<string, object> Reader)> objectWriter = new Dictionary<Type, (Func<object, string> Writer, Func<string, object> Reader)>();
        private readonly Dictionary<object, Guid> referenceReader = new Dictionary<object, Guid>(new ReferenceEqualityComparer());
        private readonly Dictionary<Guid, object> idLookup = new Dictionary<Guid, object>();

        public Serelizer()
        {
            // Add Stdandard Parser

            AddParser(str => Guid.Parse(str), g => g.ToString());
            AddParser(str => Convert.FromBase64String(str), b => Convert.ToBase64String(b));
            AddParser(str => str, str => str);
            AddParser(str => int.Parse(str), i => i.ToString());
            AddParser(str => bool.Parse(str), i => i.ToString());
            AddParser(str => Int16.Parse(str), i => i.ToString());
            AddParser(str => Int64.Parse(str), i => i.ToString());
            AddParser(str => UInt32.Parse(str), i => i.ToString());
            AddParser(str => UInt16.Parse(str), i => i.ToString());
            AddParser(str => UInt64.Parse(str), i => i.ToString());
            AddParser(str => BigInteger.Parse(str), i => i.ToString());
        }

        private void AddParser<T>(Func<string, T> parse, Func<T, String> write)
        {
            this.objectWriter.Add(typeof(T), (i => write((T)i), p => parse(p)));
        }

        private T GenerateObject<T>()
        {
            return (T)GenerateObject(typeof(T));
        }
        private Object GenerateObject(Type type)
        {

            //if (factoryLookup.ContainsKey(type))
            //    return factoryLookup[type]();
            //if (type.GenericTypeArguments.Length > 0 && genericFactoryLookup.ContainsKey(type.GetGenericTypeDefinition()))
            //    return genericFactoryLookup[type.GetGenericTypeDefinition()](type.GenericTypeArguments);

            var con = type.GetTypeInfo().DeclaredConstructors.FirstOrDefault(x => x.GetParameters().Length == 0);
            if (con != null)
                return con.Invoke(new Object[0]);

            throw new ArgumentException("Cannot Create Instance of " + type);

        }
        public string Serelize(AbstractScripture obj, bool includeSecrets)
        {
            this.idLookup.Clear();
            this.referenceReader.Clear();
            var b = new StringBuilder();
            var type = obj.GetType();
            using (var writer = System.Xml.XmlWriter.Create(b))
            {

                writer.WriteStartDocument();
                writer.WriteStartElement("Scripture");
                writer.WriteAttributeString("IncludeSecrets", includeSecrets.ToString());
                writer.WriteAttributeString("Type", type.AssemblyQualifiedName);

                Serelize(writer, obj, type.Name, includeSecrets);

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return b.ToString();
        }

        public T Deserelize<T>(string xml) where T : AbstractScripture
        {
            this.idLookup.Clear();
            this.referenceReader.Clear();
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            var type = typeof(T);
            bool includeSecrets = false;
            if (root.Attribute("IncludeSecrets") != null)
                includeSecrets = bool.Parse(root.FirstAttribute.Value);
            if (root.Attribute("Type") != null)
                type = Type.GetType(root.Attribute("Type").Value);

            if (!typeof(T).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
                throw new ArgumentException($"Xml has Incompatible type.({type.AssemblyQualifiedName} is not assignable to {typeof(T).AssemblyQualifiedName})");

            var obj = GenerateObject(type) as AbstractScripture;

            var e = root.Elements().Single();

            Deserelize(e, obj, type, includeSecrets);

            return (T)obj;
        }

        private void Serelize(XmlWriter writer, AbstractScripture obj, string name, bool includeSecrets)
        {
            writer.WriteStartElement(name);
            writer.WriteAttributeString("Type", obj.GetType().AssemblyQualifiedName);
            writer.WriteAttributeString("Hash", Convert.ToBase64String(obj.Hash));

            var enumerable = GetScriptusPropertysToSerelize(obj.GetType(), includeSecrets);

            foreach (var p in enumerable)
                SerelizeAsync(writer, p.GetValue(obj), p.Name, p.PropertyType);

            var subSciptus = GetSubScriptusPropertysToSerelize(obj.GetType());
            foreach (var s in subSciptus.singelPropertys)
                Serelize(writer, s.GetValue(obj) as AbstractScripture, s.Name, includeSecrets);

            foreach (var s in subSciptus.collectionPropertys)
            {
                writer.WriteStartElement(s.Name);

                var sGenerictype = GetTypeFromCollectionType(s.PropertyType);

                foreach (var item in s.GetValue(obj) as IEnumerable<AbstractScripture>)
                    Serelize(writer, item, sGenerictype.Name, includeSecrets);
                writer.WriteEndElement();
            }


            writer.WriteEndElement();
        }

        private (IEnumerable<PropertyInfo> singelPropertys, IEnumerable<PropertyInfo> collectionPropertys) GetSubScriptusPropertysToSerelize(Type type)
        {


            var singelSubScriptures = type.GetRuntimeProperties()
                .Where(x => typeof(AbstractScripture).GetTypeInfo().IsAssignableFrom(x.PropertyType.GetTypeInfo()))
                .OrderBy(x => x.Name);
            var multiSubScriptures = type.GetRuntimeProperties()
                .Where(x => typeof(IEnumerable<AbstractScripture>).GetTypeInfo().IsAssignableFrom(x.PropertyType.GetTypeInfo()))
                .Where(x => x.Name != nameof(AbstractScripture.SubScripture))
                .OrderBy(x => x.Name);

            return (singelSubScriptures, multiSubScriptures);


        }

        private void Deserelize(XElement e, AbstractScripture obj, Type type, bool includeSecrets)
        {
            if (obj.GetType() != type)
                throw new Exception();
            var enumerable = GetScriptusPropertysToSerelize(type, includeSecrets);


            var hash = Convert.FromBase64String(e.Attribute("Hash").Value);

            var childs = e.Elements().GetEnumerator();

            foreach (var p in enumerable)
            {
                if (!childs.MoveNext())
                    throw new Exception();
                var currentElement0 = childs.Current;
                if (currentElement0.Name != p.Name)
                    throw new Exception();
                var originalObject = p.GetValue(obj);
                var newObject = Deserelize(currentElement0, p.PropertyType, originalObject);
                p.SetValue(obj, newObject);
            }


            var subSciptus = GetSubScriptusPropertysToSerelize(obj.GetType());

            bool hasMoreElemets = childs.MoveNext();
            var currentElement = childs.Current;


            foreach (var s in subSciptus.singelPropertys)
            {
                if (!hasMoreElemets)
                    break;
                if (currentElement.Name != s.Name)
                    continue;

                var typeAttribute = currentElement.Attribute("Type");
                var t = System.Type.GetType(typeAttribute.Value);
                var ctor = t.GetTypeInfo().DeclaredConstructors.Single(x => x.GetParameters().Length == 0);
                var newObject = ctor.Invoke(new object[0]) as AbstractScripture;
                Deserelize(currentElement, newObject, t, includeSecrets);
                s.SetValue(obj, newObject);

                hasMoreElemets = childs.MoveNext();
                currentElement = childs.Current;

            }

            foreach (var s in subSciptus.collectionPropertys)
            {
                if (!hasMoreElemets)
                    break;
                if (currentElement.Name != s.Name)
                    continue;

                var subElements = currentElement.Elements();

                var sGenerictype = GetTypeFromCollectionType(s.PropertyType);


                if (!subElements.All(x => x.Name == sGenerictype.Name))
                    throw new Exception();

                var newList = new List<AbstractScripture>();
                foreach (var subElement in subElements)
                {
                    var typeAttribute = subElement.Attribute("Type");
                    var t = System.Type.GetType(typeAttribute.Value);
                    var ctor = t.GetTypeInfo().DeclaredConstructors.Single(x => x.GetParameters().Length == 0);
                    var newObject = ctor.Invoke(new object[0]) as AbstractScripture;
                    Deserelize(subElement, newObject, t, includeSecrets);
                    newList.Add(newObject);
                }
                var oringalObject = s.GetValue(obj);
                var list = SetOrAddObjects(s.PropertyType, newList, oringalObject);
                if (list != null)
                    s.SetValue(obj, list);

                hasMoreElemets = childs.MoveNext();
                currentElement = childs.Current;

            }

            obj.AfterDeserelize(hash, includeSecrets);
        }

        private object Deserelize(XElement element, Type type, object originalObject)
        {
            if (element.IsEmpty && !typeof(IEnumerable<object>).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()) && element.Attribute("ref") == null)
                return null;

            if (this.objectWriter.ContainsKey(type))
            {
                return this.objectWriter[type].Reader(element.Value);
            }
            else if (type.GetTypeInfo().GetCustomAttribute<System.Runtime.Serialization.DataContractAttribute>() != null)
            {
                var serelizer = new System.Runtime.Serialization.DataContractSerializer(type);
                using (var reader = element.Elements().Single().CreateReader())
                    return serelizer.ReadObject(reader);
            }
            else if (typeof(IEnumerable<object>).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
            {

                var enumerableType = GetTypeFromCollectionType(type);

                if (!element.Elements().All(x => x.Name == enumerableType.Name))
                    throw new Exception();

                var objToAdd = new List<object>();

                foreach (var e in element.Elements())
                {
                    var newObject = Deserelize(e, enumerableType, null);
                    objToAdd.Add(newObject);
                }

                var obj = SetOrAddObjects(type, objToAdd, originalObject);
                return obj;

            }
            else if (type.GetTypeInfo().IsEnum)
            {
                return Enum.Parse(type, element.Value);
            }
            else // Serelize the Propertys
            {
                var refAttribute = element.Attribute("ref");
                var idAttribute = element.Attribute("id");
                if (refAttribute != null)
                {
                    return this.idLookup[Guid.Parse(refAttribute.Value)];
                }
                else
                {
                    var propertys = GetPropertys(type);

                    var obj = GenerateObject(type);
                    this.idLookup.Add(Guid.Parse(idAttribute.Value), obj);

                    var childs = element.Elements().GetEnumerator();

                    foreach (var p in propertys)
                    {
                        if (p.GetIndexParameters().Length > 0)
                            continue;
                        if (!childs.MoveNext())
                            throw new Exception();

                        var currentElement = childs.Current;
                        if (currentElement.Name != p.Name)
                            throw new Exception();

                        var originalPropertyObject = p.GetValue(obj);
                        var newObject = Deserelize(currentElement, p.PropertyType, originalPropertyObject);

                        try
                        {
                            if (newObject != null)
                                p.SetValue(obj, newObject);
                        }
                        catch (System.ArgumentException e)
                        {
                            throw new ArgumentException($"The Property {p.Name}  on the Type {p.DeclaringType} has no Set Method.", e);
                        }
                    }
                    return obj;
                }
            }
        }

        private static Type GetTypeFromCollectionType(Type type)
        {
            Type enumerableType;
            if (type.GetTypeInfo().IsGenericType && type.GetTypeInfo().GetGenericTypeDefinition() == typeof(IEnumerable<>))
                enumerableType = type.GetTypeInfo().GenericTypeArguments[0];
            else
                enumerableType = type.GetTypeInfo().ImplementedInterfaces.First(x => x.GetTypeInfo().IsGenericType && x.GetTypeInfo().GetGenericTypeDefinition() == typeof(IEnumerable<>)).GenericTypeArguments[0];
            return enumerableType;
        }

        private object SetOrAddObjects(Type type, IEnumerable<object> objToAdd, object collection)
        {

            Type enumerableType;
            if (type.GetTypeInfo().IsGenericType && type.GetTypeInfo().GetGenericTypeDefinition() == typeof(IEnumerable<>))
                enumerableType = type.GetTypeInfo().GenericTypeArguments[0];
            else
                enumerableType = type.GetTypeInfo().ImplementedInterfaces.First(x => x.GetTypeInfo().IsGenericType && x.GetTypeInfo().GetGenericTypeDefinition() == typeof(IEnumerable<>)).GenericTypeArguments[0];

            if (collection != null
                && typeof(ICollection<>).MakeGenericType(enumerableType).GetTypeInfo().IsAssignableFrom(collection.GetType().GetTypeInfo())
                && !(bool)typeof(ICollection<>).MakeGenericType(enumerableType).GetRuntimeProperty(nameof(ICollection<object>.IsReadOnly)).GetValue(collection))
            {
                var method = typeof(ICollection<>).MakeGenericType(enumerableType).GetRuntimeMethod(nameof(ICollection<object>.Add), new Type[] { enumerableType });
                foreach (var o in objToAdd)
                    method.Invoke(collection, new object[] { o });
                return null;
            }
            else if (typeof(ICollection<>).MakeGenericType(enumerableType).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()) && !type.IsArray)
            {
                var obj = GenerateObject(type);
                var method = typeof(ICollection<>).MakeGenericType(enumerableType).GetRuntimeMethod(nameof(ICollection<object>.Add), new Type[] { enumerableType });
                foreach (var o in objToAdd)
                    method.Invoke(obj, new object[] { o });
                return obj;
            }
            else if (typeof(IEnumerable<>).MakeGenericType(enumerableType) == type || type.IsArray)
            {
                var oldArray = objToAdd.ToArray();
                var newArray = Array.CreateInstance(enumerableType, oldArray.Length);
                for (int i = 0; i < oldArray.Length; i++)
                    newArray.SetValue(oldArray[i], i);
                return newArray;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private void SerelizeAsync(XmlWriter writer, object obj, string name, Type type)
        {

            if (obj == null)
            {
                writer.WriteElementString(name, "");
                return;
            }


            if (obj is AbstractScripture)
                throw new ArgumentException("Wrongly Serelisation of AbstractScripture (May not be marked with Value Attribute).");

            else if (this.objectWriter.ContainsKey(type))
            {
                var dataToWrite = this.objectWriter[type].Writer(obj);
                writer.WriteElementString(name, dataToWrite);
            }
            else if (type.GetTypeInfo().GetCustomAttribute<System.Runtime.Serialization.DataContractAttribute>() != null)
            {
                var serelizer = new System.Runtime.Serialization.DataContractSerializer(type);

                writer.WriteStartElement(name);
                serelizer.WriteObject(writer, obj);
                writer.WriteEndElement();
            }
            else if (typeof(IEnumerable<object>).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
            {
                Type enumerableType;
                if (type.GetTypeInfo().IsGenericType && type.GetTypeInfo().GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    enumerableType = type.GetTypeInfo().GenericTypeArguments[0];
                else
                    enumerableType = type.GetTypeInfo().ImplementedInterfaces.First(x => x.GetTypeInfo().IsGenericType && x.GetTypeInfo().GetGenericTypeDefinition() == typeof(IEnumerable<>)).GenericTypeArguments[0];

                writer.WriteStartElement(name);
                foreach (var element in obj as IEnumerable<object>)
                    SerelizeAsync(writer, element, enumerableType.Name, enumerableType);
                writer.WriteEndElement();
            }
            else if (type.GetTypeInfo().IsEnum)
            {
                writer.WriteElementString(name, obj.ToString());
            }
            else // Serelize the Propertys
            {
                writer.WriteStartElement(name);

                if (this.referenceReader.ContainsKey(obj))
                {
                    writer.WriteAttributeString("ref", this.referenceReader[obj].ToString());
                }
                else
                {
                    this.referenceReader.Add(obj, Guid.NewGuid());
                    writer.WriteAttributeString("id", this.referenceReader[obj].ToString());

                    var propertys = GetPropertys(type);


                    foreach (var p in propertys)
                    {
                        if (p.GetIndexParameters().Length > 0)
                            continue;
                        SerelizeAsync(writer, p.GetValue(obj), p.Name, p.PropertyType);
                    }
                }

                writer.WriteEndElement();
            }
        }

        private static List<PropertyInfo> GetPropertys(Type type)
        {
            var propertys = new List<PropertyInfo>();

            if (type.GetTypeInfo().IsInterface)
            {
                propertys.AddRange(type.GetTypeInfo().DeclaredProperties.Where(x => x.GetCustomAttribute<System.Runtime.Serialization.IgnoreDataMemberAttribute>(true) == null));
                propertys.AddRange(type.GetTypeInfo().ImplementedInterfaces.SelectMany(x => x.GetTypeInfo().DeclaredProperties).Where(x => x.GetCustomAttribute<System.Runtime.Serialization.IgnoreDataMemberAttribute>(true) == null));
            }
            else
            {
                propertys.AddRange(type.GetRuntimeProperties().Where(x => x.GetCustomAttribute<System.Runtime.Serialization.IgnoreDataMemberAttribute>(true) == null));
            }

            return propertys;
        }

        private static IEnumerable<PropertyInfo> GetScriptusPropertysToSerelize(Type type, bool includeSecrets)
        {
            var propertysToSerelize = type.GetRuntimeProperties().Select(x => new { Property = x, ScriptureValue = x.GetCustomAttribute<ScriptureValueAttribute>()?.ValueType })
                            .Where(x => x.ScriptureValue.HasValue);

            if (!includeSecrets)
                propertysToSerelize = propertysToSerelize.Where(x => x.ScriptureValue == ScriptureValueType.Public);
            var enumerable = propertysToSerelize.Select(x => x.Property).OrderBy(x => x.Name);
            return enumerable;
        }

    }
}
