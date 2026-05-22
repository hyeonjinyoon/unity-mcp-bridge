using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace UnityMcpBridge.Editor.Handlers
{
    public static class InputHandler
    {
        public static string Tap(JObject body)
        {
            var x = body.Value<float>("x");
            var inputY = body.Value<float>("y");
            // 입력은 좌상단 (0,0) ~ 우하단 (Screen.width, Screen.height) 이미지 좌표.
            // Unity screen 좌표(좌하단 origin)로 변환.
            var y = Screen.height - inputY;

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return McpUtils.Error("No EventSystem found in scene");

            var pointerData = new PointerEventData(eventSystem)
            {
                position = new Vector2(x, y)
            };

            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);

            if (results.Count == 0)
                return McpUtils.Success(new { hit = false, position = new { x, y = inputY } });

            var target = results[0].gameObject;
            pointerData.pointerCurrentRaycast = results[0];
            pointerData.pointerPressRaycast = results[0];

            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            var pressTarget = clickHandler != null ? clickHandler : target;

            pointerData.pointerPress = pressTarget;
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerUpHandler);

            McpCursorOverlay.Show(x, y);

            return McpUtils.Success(new
            {
                hit = true,
                target = target.name,
                clickHandler = clickHandler != null ? clickHandler.name : null,
                position = new { x, y = inputY }
            });
        }

        public static string Hold(JObject body)
        {
            var x = body.Value<float>("x");
            var inputY = body.Value<float>("y");
            var y = Screen.height - inputY;
            var duration = body.Value<float>("duration");

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return McpUtils.Error("No EventSystem found in scene");

            var pointerData = new PointerEventData(eventSystem)
            {
                position = new Vector2(x, y)
            };

            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);

            if (results.Count == 0)
                return McpUtils.Success(new { hit = false, position = new { x, y = inputY }, clicks = 0 });

            var target = results[0].gameObject;
            pointerData.pointerCurrentRaycast = results[0];
            pointerData.pointerPressRaycast = results[0];

            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            var pressTarget = clickHandler != null ? clickHandler : target;
            pointerData.pointerPress = pressTarget;

            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerDownHandler);

            var clickCount = Mathf.Max(1, Mathf.RoundToInt(duration * 50));
            for (var i = 0; i < clickCount; i++)
                ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerClickHandler);

            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerUpHandler);

            McpCursorOverlay.Show(x, y);

            return McpUtils.Success(new
            {
                hit = true,
                target = target.name,
                clickHandler = clickHandler != null ? clickHandler.name : null,
                position = new { x, y = inputY },
                clicks = clickCount
            });
        }

        public static string Swipe(JObject body)
        {
            var startX = body.Value<float>("startX");
            var inputStartY = body.Value<float>("startY");
            var endX = body.Value<float>("endX");
            var inputEndY = body.Value<float>("endY");
            var startY = Screen.height - inputStartY;
            var endY = Screen.height - inputEndY;
            var steps = body["steps"] != null ? body.Value<int>("steps") : 20;
            if (steps < 2) steps = 2;

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return McpUtils.Error("No EventSystem found in scene");

            var startPos = new Vector2(startX, startY);
            var endPos = new Vector2(endX, endY);

            var pointerData = new PointerEventData(eventSystem)
            {
                position = startPos,
                pressPosition = startPos,
                delta = Vector2.zero,
                button = PointerEventData.InputButton.Left
            };

            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);

            if (results.Count == 0)
            {
                return McpUtils.Success(new
                {
                    hit = false,
                    start = new { x = startX, y = inputStartY },
                    end = new { x = endX, y = inputEndY }
                });
            }

            var target = results[0].gameObject;
            pointerData.pointerCurrentRaycast = results[0];
            pointerData.pointerPressRaycast = results[0];

            var dragHandler = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            var dragTarget = dragHandler != null ? dragHandler : target;
            pointerData.pointerPress = dragTarget;
            pointerData.pointerDrag = dragTarget;

            ExecuteEvents.Execute(dragTarget, pointerData, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(dragTarget, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(dragTarget, pointerData, ExecuteEvents.beginDragHandler);

            var prevPos = startPos;
            for (var i = 1; i <= steps; i++)
            {
                var t = (float)i / steps;
                var curPos = Vector2.Lerp(startPos, endPos, t);
                pointerData.position = curPos;
                pointerData.delta = curPos - prevPos;

                var stepResults = new List<RaycastResult>();
                eventSystem.RaycastAll(pointerData, stepResults);
                pointerData.pointerCurrentRaycast = stepResults.Count > 0 ? stepResults[0] : default;

                ExecuteEvents.Execute(dragTarget, pointerData, ExecuteEvents.dragHandler);
                prevPos = curPos;
            }

            pointerData.delta = Vector2.zero;
            ExecuteEvents.Execute(dragTarget, pointerData, ExecuteEvents.endDragHandler);
            ExecuteEvents.Execute(dragTarget, pointerData, ExecuteEvents.pointerUpHandler);
            pointerData.pointerDrag = null;

            McpCursorOverlay.Show(endX, endY);

            return McpUtils.Success(new
            {
                hit = true,
                target = target.name,
                dragHandler = dragHandler != null ? dragHandler.name : null,
                start = new { x = startX, y = inputStartY },
                end = new { x = endX, y = inputEndY },
                steps
            });
        }

        public static string Raycast(JObject body)
        {
            var x = body.Value<float>("x");
            var inputY = body.Value<float>("y");
            var y = Screen.height - inputY;

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return McpUtils.Error("No EventSystem found in scene");

            var pointerData = new PointerEventData(eventSystem)
            {
                position = new Vector2(x, y)
            };

            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);

            var hits = new List<object>();
            foreach (var r in results)
            {
                hits.Add(new
                {
                    name = r.gameObject.name,
                    depth = r.depth,
                    sortingLayer = r.sortingLayer,
                    sortingOrder = r.sortingOrder
                });
            }

            return McpUtils.Success(new { position = new { x, y = inputY }, hits });
        }

        public static string PressKey(JObject body)
        {
            var keyName = body.Value<string>("key");
            if (string.IsNullOrEmpty(keyName))
                return McpUtils.Error("'key' parameter is required");

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return McpUtils.Error("No keyboard device found");

            if (!Enum.TryParse<Key>(keyName, true, out var key))
                return McpUtils.Error($"Unknown key: {keyName}. Use Key enum names (e.g. Escape, Enter, Space)");

            var keyControl = keyboard[key];

            using (StateEvent.From(keyboard, out var pressEvent))
            {
                keyControl.WriteValueIntoEvent(1f, pressEvent);
                InputSystem.QueueEvent(pressEvent);
            }

            var frameCount = 0;
            void ReleaseAfterFrames()
            {
                if (++frameCount < 3)
                    return;
                EditorApplication.update -= ReleaseAfterFrames;
                var kb = Keyboard.current;
                if (kb == null) return;
                using (StateEvent.From(kb, out var releaseEvent))
                {
                    kb[key].WriteValueIntoEvent(0f, releaseEvent);
                    InputSystem.QueueEvent(releaseEvent);
                }
            }
            EditorApplication.update += ReleaseAfterFrames;

            return McpUtils.Success(new { key = keyName, pressed = true });
        }
    }
}
