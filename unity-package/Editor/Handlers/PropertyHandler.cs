using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Handlers
{
    public static class PropertyHandler
    {
        public static string Get(JObject body)
        {
            var (so, prop, err) = Resolve(body);
            if (err != null) return err;

            using (so)
            {
                return McpUtils.Success(new
                {
                    propertyPath = prop.propertyPath,
                    propertyType = prop.propertyType.ToString(),
                    value = ReadValue(prop)
                });
            }
        }

        public static string Set(JObject body)
        {
            var (so, prop, err) = Resolve(body);
            if (err != null) return err;

            using (so)
            {
                var value = body["value"];
                if (value == null)
                    return McpUtils.Error("value is required");

                if (!WriteValue(prop, value))
                    return McpUtils.Error($"Unsupported property type: {prop.propertyType}");

                so.ApplyModifiedProperties();

                return McpUtils.Success(new
                {
                    propertyPath = prop.propertyPath,
                    propertyType = prop.propertyType.ToString(),
                    value = ReadValue(prop)
                });
            }
        }

        private static (SerializedObject, SerializedProperty, string) Resolve(JObject body)
        {
            var path = body.Value<string>("path");
            var componentType = body.Value<string>("component");
            var propertyPath = body.Value<string>("property");

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(componentType) || string.IsNullOrEmpty(propertyPath))
                return (null, null, McpUtils.Error("path, component, and property are required"));

            var go = McpUtils.FindByPath(path);
            if (go == null)
                return (null, null, McpUtils.Error($"Object not found: {path}"));

            SerializedObject so;
            if (componentType == "GameObject")
            {
                so = new SerializedObject(go);
            }
            else
            {
                var type = McpUtils.FindType(componentType);
                if (type == null)
                    return (null, null, McpUtils.Error($"Component type not found: {componentType}"));

                var component = go.GetComponent(type);
                if (component == null)
                    return (null, null, McpUtils.Error($"Component not found on object: {componentType}"));

                so = new SerializedObject(component);
            }
            var prop = so.FindProperty(propertyPath);
            if (prop == null)
            {
                so.Dispose();
                return (null, null, McpUtils.Error($"Property not found: {propertyPath}"));
            }

            return (so, prop, null);
        }

        private static object ReadValue(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.intValue,
                SerializedPropertyType.Boolean => prop.boolValue,
                SerializedPropertyType.Float => prop.floatValue,
                SerializedPropertyType.String => prop.stringValue,
                SerializedPropertyType.Color => new
                {
                    r = prop.colorValue.r,
                    g = prop.colorValue.g,
                    b = prop.colorValue.b,
                    a = prop.colorValue.a
                },
                SerializedPropertyType.Vector2 => new
                {
                    x = prop.vector2Value.x,
                    y = prop.vector2Value.y
                },
                SerializedPropertyType.Vector3 => new
                {
                    x = prop.vector3Value.x,
                    y = prop.vector3Value.y,
                    z = prop.vector3Value.z
                },
                SerializedPropertyType.Vector4 => new
                {
                    x = prop.vector4Value.x,
                    y = prop.vector4Value.y,
                    z = prop.vector4Value.z,
                    w = prop.vector4Value.w
                },
                SerializedPropertyType.Rect => new
                {
                    x = prop.rectValue.x,
                    y = prop.rectValue.y,
                    width = prop.rectValue.width,
                    height = prop.rectValue.height
                },
                SerializedPropertyType.Enum => prop.enumValueIndex,
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue != null ? prop.objectReferenceValue.name : null,
                SerializedPropertyType.LayerMask => prop.intValue,
                _ => $"<{prop.propertyType}>"
            };
        }

        private static bool WriteValue(SerializedProperty prop, JToken value)
        {
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        prop.intValue = value.Value<int>();
                        return true;
                    case SerializedPropertyType.Boolean:
                        prop.boolValue = value.Value<bool>();
                        return true;
                    case SerializedPropertyType.Float:
                        prop.floatValue = value.Value<float>();
                        return true;
                    case SerializedPropertyType.String:
                        prop.stringValue = value.Value<string>();
                        return true;
                    case SerializedPropertyType.Color:
                        prop.colorValue = new Color(
                            value.Value<float>("r"),
                            value.Value<float>("g"),
                            value.Value<float>("b"),
                            value["a"] != null ? value.Value<float>("a") : 1f);
                        return true;
                    case SerializedPropertyType.Vector2:
                        prop.vector2Value = new Vector2(
                            value.Value<float>("x"),
                            value.Value<float>("y"));
                        return true;
                    case SerializedPropertyType.Vector3:
                        prop.vector3Value = new Vector3(
                            value.Value<float>("x"),
                            value.Value<float>("y"),
                            value.Value<float>("z"));
                        return true;
                    case SerializedPropertyType.Vector4:
                        prop.vector4Value = new Vector4(
                            value.Value<float>("x"),
                            value.Value<float>("y"),
                            value.Value<float>("z"),
                            value.Value<float>("w"));
                        return true;
                    case SerializedPropertyType.Rect:
                        prop.rectValue = new Rect(
                            value.Value<float>("x"),
                            value.Value<float>("y"),
                            value.Value<float>("width"),
                            value.Value<float>("height"));
                        return true;
                    case SerializedPropertyType.Enum:
                        prop.enumValueIndex = value.Value<int>();
                        return true;
                    case SerializedPropertyType.LayerMask:
                        prop.intValue = value.Value<int>();
                        return true;
                    case SerializedPropertyType.ObjectReference:
                        return WriteObjectReference(prop, value);
                    default:
                        return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Type GetExpectedObjectType(SerializedProperty prop)
        {
            var targetObject = prop.serializedObject.targetObject;
            var type = targetObject.GetType();
            var field = type.GetField(prop.propertyPath,
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);
            return field?.FieldType;
        }

        private static bool WriteObjectReference(SerializedProperty prop, JToken value)
        {
            if (value.Type == JTokenType.Null)
            {
                prop.objectReferenceValue = null;
                return true;
            }

            var expectedType = GetExpectedObjectType(prop);

            if (value.Type == JTokenType.Integer)
            {
                var obj = EditorUtility.EntityIdToObject(value.Value<int>());
                if (obj == null) return false;
                return AssignWithCoercion(prop, obj, expectedType);
            }

            if (value.Type == JTokenType.Object)
            {
                var pathStr = value["path"]?.Value<string>();
                if (string.IsNullOrEmpty(pathStr)) return false;
                var go = McpUtils.FindByPath(pathStr);
                if (go == null) return false;

                var componentName = value["component"]?.Value<string>();
                if (!string.IsNullOrEmpty(componentName))
                {
                    var compType = McpUtils.FindType(componentName);
                    if (compType == null) return false;
                    var comp = go.GetComponent(compType);
                    if (comp == null) return false;
                    prop.objectReferenceValue = comp;
                    return true;
                }
                return AssignWithCoercion(prop, go, expectedType);
            }

            if (value.Type == JTokenType.String)
            {
                var inputStr = value.Value<string>();
                if (string.IsNullOrEmpty(inputStr))
                {
                    prop.objectReferenceValue = null;
                    return true;
                }

                if (inputStr.StartsWith("Assets/") || inputStr.StartsWith("Packages/"))
                {
                    var asset = expectedType != null
                        ? AssetDatabase.LoadAssetAtPath(inputStr, expectedType)
                        : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(inputStr);
                    if (asset == null) return false;
                    prop.objectReferenceValue = asset;
                    return true;
                }

                var go = McpUtils.FindByPath(inputStr);
                if (go == null) return false;
                return AssignWithCoercion(prop, go, expectedType);
            }

            return false;
        }

        private static bool AssignWithCoercion(SerializedProperty prop, UnityEngine.Object obj, Type expectedType)
        {
            if (expectedType != null && obj is GameObject go && typeof(Component).IsAssignableFrom(expectedType))
            {
                var comp = go.GetComponent(expectedType);
                if (comp == null) return false;
                obj = comp;
            }
            prop.objectReferenceValue = obj;
            return true;
        }
    }
}
