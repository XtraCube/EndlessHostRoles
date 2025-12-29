using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Hazel;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using UnityEngine;

namespace EHR.Modules;

public abstract class GameOptionsSender
{
    protected abstract bool IsDirty { get; set; }

    protected virtual void SendGameOptions()
    {
        var stopwatch = new Stopwatch();
        IGameOptions opt = BuildGameOptions();

        // option => byte[]
        MessageWriter writer = MessageWriter.Get();
        writer.Write(opt.Version);
        writer.StartMessage(0);
        writer.Write((byte)opt.GameMode);

        if (opt.TryCast(out NormalGameOptionsV10 normalOpt))
            NormalGameOptionsV10.Serialize(writer, normalOpt);
        else if (opt.TryCast(out HideNSeekGameOptionsV10 hnsOpt))
            HideNSeekGameOptionsV10.Serialize(writer, hnsOpt);
        else
        {
            Logger.Error("Option cast failed", ToString());
        }

        writer.EndMessage();

        Il2CppStructArray<byte> optionArray = writer.ToByteArray(false);
        writer.Recycle();
        SendOptionsArray(optionArray);
    }

    private readonly Queue<Il2CppStructArray<byte>> _optionsQueue = new();
    private Coroutine _activeCoroutine;

    protected virtual void SendOptionsArray(Il2CppStructArray<byte> optionArray)
    {
        if (optionArray == null || optionArray.Length == 0)
        {
            return;
        }

        const int maxQueueSize = 20;
        if (_optionsQueue.Count >= maxQueueSize)
        {
            Main.logSource.LogWarning("Options queue is full, dropping oldest option array");
            _optionsQueue.Dequeue();
        }

        _optionsQueue.Enqueue(optionArray);
        _activeCoroutine ??= GameManager.Instance.StartCoroutine(ProcessQueue().WrapToIl2Cpp());
    }

    private IEnumerator ProcessQueue()
    {
        while (_optionsQueue.Count > 0)
        {
            Il2CppStructArray<byte> optionArray = _optionsQueue.Dequeue();
            yield return CoSendOptionsArray(optionArray);
        }
        _activeCoroutine = null;
    }

    private static IEnumerator CoSendOptionsArray(Il2CppStructArray<byte> optionArray)
    {
        int count = GameManager.Instance.LogicComponents.Count;
        for (int i = 0; i < count; i++)
        {
            Il2CppSystem.Object logicComponent = GameManager.Instance.LogicComponents[(Index)i];
            if (logicComponent.TryCast<LogicOptions>(out _))
                SendOptionsArray(optionArray, (byte)i, -1);
            yield return null;
        }
    }

    protected static void SendOptionsArray(Il2CppStructArray<byte> optionArray, byte LogicOptionsIndex, int targetClientId)
    {
        try
        {
            MessageWriter writer = MessageWriter.Get(SendOption.Reliable);

            writer.StartMessage(targetClientId == -1 ? Tags.GameData : Tags.GameDataTo);
            {
                writer.Write(AmongUsClient.Instance.GameId);
                if (targetClientId != -1) writer.WritePacked(targetClientId);

                writer.StartMessage(1);
                {
                    writer.WritePacked(GameManager.Instance.NetId);
                    writer.StartMessage(LogicOptionsIndex);
                    {
                        writer.WriteBytesAndSize(optionArray);
                    }
                    writer.EndMessage();
                }
                writer.EndMessage();
            }

            writer.EndMessage();

            AmongUsClient.Instance.SendOrDisconnect(writer);
            writer.Recycle();
        }
        catch (Exception ex) { Logger.Fatal(ex.ToString(), "GameOptionsSender.SendOptionsArray"); }
    }

    public abstract IGameOptions BuildGameOptions();

    protected virtual bool AmValid()
    {
        return true;
    }

    #region Static

    public static readonly List<GameOptionsSender> AllSenders = [new NormalGameOptionsSender()];

    public static IEnumerator SendAllGameOptionsAsync()
    {
        AllSenders.RemoveAll(s => s == null || !s.AmValid());

        for (var index = 0; index < AllSenders.Count; index++)
        {
            if (index >= AllSenders.Count) yield break; // Safety check
            GameOptionsSender sender = AllSenders[index];

            if (sender.IsDirty)
            {
                sender.SendGameOptions();
                yield return null;
            }

            sender.IsDirty = false;
        }
    }

    public static void SendAllGameOptions()
    {
        AllSenders.RemoveAll(s => s == null || !s.AmValid());

        // ReSharper disable once ForCanBeConvertedToForeach
        for (var index = 0; index < AllSenders.Count; index++)
        {
            if (index >= AllSenders.Count) return; // Safety check
            GameOptionsSender sender = AllSenders[index];
            if (sender.IsDirty) sender.SendGameOptions();

            sender.IsDirty = false;
        }
    }

    #endregion
}