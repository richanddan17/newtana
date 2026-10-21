using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Cinemachine;

public static class RoomSystemSetup
{
    const string MenuPath = "newtana/룸 배치";
    const float RoomWidth = 40f;
    const float RoomHeight = 20f;
    const float CorridorWidth = 4f;
    const float CorridorHeight = 20f;
    const int RoomCount = 3;
    const int CorridorCount = 2;

    [MenuItem(MenuPath, false, 101)]
    public static void Setup()
    {
        ProjectTagsLayersSetup.EnsureAllQuiet();

        RoomManager roomManager = Object.FindAnyObjectByType<RoomManager>(FindObjectsInactive.Include);
        if (roomManager == null)
        {
            var rmGo = new GameObject("RoomManager");
            Undo.RegisterCreatedObjectUndo(rmGo, MenuPath);
            roomManager = Undo.AddComponent<RoomManager>(rmGo);
        }

        Transform parent = roomManager.transform;
        List<Room> rooms = new List<Room>(RoomCount);

        for (int i = 0; i < RoomCount; i++)
        {
            string roomName = $"Room_{i + 1}";
            Room room = FindOrCreateRoom(parent, roomName);
            room.name = roomName;
            Undo.RecordObject(room.transform, MenuPath);
            room.transform.localPosition = new Vector3(i * (RoomWidth + CorridorWidth), 0f, 0f);
            ConfigureRoom(room, RoomWidth, RoomHeight, i == 0);
            rooms.Add(room);
        }

        for (int i = 0; i < CorridorCount; i++)
        {
            string corridorName = $"Corridor_{i + 1}";
            Transform corridor = FindOrCreateCorridor(parent, corridorName);
            Undo.RecordObject(corridor, MenuPath);
            // 통로는 방 사이 빈 구간을 정확히 채운다: 방1[-20,20]·방2[24,64]·방3[68,108] → 통로 중심 22, 66
            corridor.localPosition = new Vector3(
                RoomWidth * 0.5f + CorridorWidth * 0.5f + i * (RoomWidth + CorridorWidth), 0f, 0f);
            ConfigureCorridor(corridor, CorridorWidth, CorridorHeight);
        }

        WireRoomManager(roomManager, rooms);

        Debug.Log($"[룸 배치] 완료 — 방 {RoomCount}개(40×20) + 통로 {CorridorCount}개(폭 4) 가로 배치. 재실행 가능합니다.");
    }

    static Room FindOrCreateRoom(Transform parent, string name)
    {
        var existing = GameObject.Find(name);
        if (existing != null)
        {
            var room = existing.GetComponent<Room>();
            if (room != null) return room;
        }

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, MenuPath);
        go.transform.SetParent(parent, false);
        var roomComp = Undo.AddComponent<Room>(go);
        return roomComp;
    }

    static Transform FindOrCreateCorridor(Transform parent, string name)
    {
        var existing = GameObject.Find(name);
        if (existing != null) return existing.transform;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, MenuPath);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void ConfigureRoom(Room room, float width, float height, bool isFirst)
    {
        var boundsCollider = room.GetComponent<BoxCollider2D>();
        if (boundsCollider == null)
            boundsCollider = Undo.AddComponent<BoxCollider2D>(room.gameObject);
        Undo.RecordObject(boundsCollider, MenuPath);
        boundsCollider.isTrigger = true;
        boundsCollider.size = new Vector2(width, height);
        boundsCollider.offset = Vector2.zero;

        var so = new SerializedObject(room);
        var boundsProp = so.FindProperty("boundsCollider");
        if (boundsProp != null && boundsProp.objectReferenceValue == null)
            boundsProp.objectReferenceValue = boundsCollider;
        var modeProp = so.FindProperty("cameraBoundaryMode");
        if (modeProp != null && modeProp.intValue == 0)
            modeProp.intValue = (int)CameraBoundaryMode.Soft;
        var startProp = so.FindProperty("isStartingRoom");
        if (startProp != null && isFirst)
            startProp.boolValue = true;
        so.ApplyModifiedProperties();

        EnsureRoomBackground(room, width, height);
    }

    static void ConfigureCorridor(Transform corridor, float width, float height)
    {
        var col = corridor.GetComponent<BoxCollider2D>();
        if (col == null)
            col = Undo.AddComponent<BoxCollider2D>(corridor.gameObject);
        Undo.RecordObject(col, MenuPath);
        col.isTrigger = true;
        col.size = new Vector2(width, height);
        col.offset = Vector2.zero;
    }

    static void EnsureRoomBackground(Room room, float width, float height)
    {
        Transform bg = room.transform.Find("Background");
        if (bg == null)
        {
            var bgGo = new GameObject("Background");
            Undo.RegisterCreatedObjectUndo(bgGo, MenuPath);
            bgGo.transform.SetParent(room.transform, false);
            bg = bgGo.transform;
        }

        var sr = bg.GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = Undo.AddComponent<SpriteRenderer>(bg.gameObject);
        Undo.RecordObject(sr, MenuPath);
        sr.sortingLayerName = "BG_Main";
        sr.sortingOrder = -10;

        var parallax = bg.GetComponent<ParallaxLayer>();
        if (parallax == null)
            parallax = Undo.AddComponent<ParallaxLayer>(bg.gameObject);
        Undo.RecordObject(parallax, MenuPath);
        var pso = new SerializedObject(parallax);
        var px = pso.FindProperty("parallaxX");
        if (px != null) px.floatValue = 0.5f;
        var py = pso.FindProperty("parallaxY");
        if (py != null) py.floatValue = 0.3f;
        pso.ApplyModifiedProperties();

        Undo.RecordObject(bg, MenuPath);
        bg.localScale = new Vector3(width / 10f, height / 10f, 1f);
    }

    static void WireRoomManager(RoomManager roomManager, List<Room> rooms)
    {
        var so = new SerializedObject(roomManager);
        var listProp = so.FindProperty("rooms");
        if (listProp != null)
        {
            listProp.ClearArray();
            for (int i = 0; i < rooms.Count; i++)
            {
                listProp.InsertArrayElementAtIndex(i);
                listProp.GetArrayElementAtIndex(i).objectReferenceValue = rooms[i];
            }
        }
        so.ApplyModifiedProperties();
    }
}
