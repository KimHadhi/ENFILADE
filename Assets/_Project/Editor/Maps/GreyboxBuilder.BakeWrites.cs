using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// GreyboxBuilder: what the occlusion and lighting bakes could not write. One part of the class; the rest is in
// GreyboxBuilder.cs and the files beside it.
//
// The rebuild of every map on 2026-11-14 at 23:00 failed seven times to write the probe volume's cell files
// (ProbeCellWriteGuard) and four times to write a .meta beside the scene ("Cannot open file '.../OcclusionCullingData.asset.meta'
// for write", the same for LightingData). Unity logs such a failure and goes on, and the build's window still said
// "구웠어요". The editor keeps handles open to files it has read, which block its own later writes to them
// (AssetDatabase.ReleaseCachedFileHandles, which lets them go). So each bake now starts by letting them go
// (BakeOcclusion, BakeEnvironment), and the build listens for the writes the bake could not make and says in its window
// what failed and what to do (ProbeReport, MetaReport).
public sealed partial class GreyboxBuilder
{
    // How the probe volume's cell files fared in this build's bake.
    private enum ProbeWrite { Written, Undilated, NotWritten }

    private ProbeWrite probeWrite;
    // The write failures the bake logged, and the .meta files it could not write.
    private readonly List<string> bakeWriteErrors = new List<string>();
    private readonly HashSet<string> unwrittenMetas = new HashSet<string>();

    // Before a bake that writes beside the scene: Unity's cached handles go, and the bake's failed writes are heard.
    private void BeginBakeWrites()
    {
        AssetDatabase.ReleaseCachedFileHandles();
        ProbeCellWriteGuard.Notes.Clear();
        Application.logMessageReceived -= HearBakeWrite;
        Application.logMessageReceived += HearBakeWrite;
    }

    private void EndBakeWrites() => Application.logMessageReceived -= HearBakeWrite;

    // A failed write as Unity logs it: the probe bake's exception (its text holds the stack), or a .meta it could not write.
    private void HearBakeWrite(string message, string stack, LogType type)
    {
        if (type == LogType.Log || string.IsNullOrEmpty(message)) return;
        var meta = Regex.Match(message, @"(?:Cannot open file|Failed to write \.meta file) '([^']+\.meta)'");
        if (meta.Success)
        {
            unwrittenMetas.Add(meta.Groups[1].Value);
            return;
        }
        if (message.IndexOf("Win32 IO returned", System.StringComparison.Ordinal) < 0
            && message.IndexOf("IOException", System.StringComparison.Ordinal) < 0) return;
        bakeWriteErrors.Add(message);
        // The first write puts the new cells down; the second (after the dilation) only fills the probes inside walls.
        if (message.IndexOf("WriteBakingCells", System.StringComparison.Ordinal) >= 0) probeWrite = ProbeWrite.NotWritten;
        else if (message.IndexOf("WriteDilatedCells", System.StringComparison.Ordinal) >= 0 && probeWrite == ProbeWrite.Written)
            probeWrite = ProbeWrite.Undilated;
    }

    // Who still held a cell file as the probe bake wrote it (ProbeCellWriteGuard), for the window.
    private static string CellHolders()
        => ProbeCellWriteGuard.Notes.Count == 0 ? "잡은 쪽은 찾지 못했어요" : "잡고 있던 것: " + string.Join("; ", ProbeCellWriteGuard.Notes);

    // After the last save: the .meta files the bakes could not write. Unity keeps the asset's GUID in memory, and the scene
    // points at it by that GUID; if the .meta on disk says another, the scene loses the culling or lighting data the next
    // time the editor starts. When the GUIDs agree the old .meta is as good, and the window says nothing.
    private string MetaReport()
    {
        var lost = new List<string>();
        foreach (string meta in unwrittenMetas)
        {
            string asset = meta.Substring(0, meta.Length - ".meta".Length);
            string guid = AssetDatabase.AssetPathToGUID(asset);
            if (string.IsNullOrEmpty(guid) || MetaGuid(meta) == guid) continue;
            lost.Add(Path.GetFileName(asset));
        }
        return lost.Count == 0 ? ""
            : "\n.meta를 쓰지 못한 데이터: " + string.Join(", ", lost) + ". 디스크의 .meta가 편집기가 아는 것과 달라서 다음에 Unity를 켜면 "
              + "장면이 이 데이터를 잃어요. Unity를 껐다 켠 뒤 이 장을 다시 지어 주세요.";
    }

    // The guid line of a .meta on disk ("" when it can't be read).
    private static string MetaGuid(string meta)
    {
        try
        {
            foreach (string line in File.ReadLines(meta))
                if (line.StartsWith("guid: ")) return line.Substring(6).Trim();
        }
        catch (System.Exception) { }
        return "";
    }
}
