﻿using Alta.Map;
using Alta.Networking;
using Alta.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hypha.Migration
{
    // What the fuck is this abomination of a class definition
    public class ModPerPlayerManager<TThis, TContent, TSaveFormat> : PerPlayerManager<TThis, TContent, TSaveFormat>, IAutoSave where TThis : PerPlayerManager<TThis, TContent, TSaveFormat> where TContent : class, IPerPlayerContent<TSaveFormat>, new() where TSaveFormat : IAltaFileFormat, new()
    {

        public new void Awake()
        {
            if (Instance == null)
            {
                Instance = (TThis)(object)this;
                return;
            }
            if (Instance != (TThis)(object)this)
            {
                if (Instance is not ModPerPlayerManager<TThis, TContent, TSaveFormat>)
                {
                    ObjectUtility.Destroy(Instance);
                    Instance = (TThis)(object)this;
                }

                else
                {
                    ObjectUtility.Destroy(this);
                }
            }
        }

        public void AutoSave()
        {
            foreach (KeyValuePair<int, TContent> keyValuePair in contents)
            {
                keyValuePair.Value.Save(false);
            }
        }

        public virtual async Task<TContent> GetContentAsync(Player player)
        {
            TContent tcontent;
            if (player == null)
            {
                logger.Error("Cannot get content manager for null player.");
                tcontent = default;
            }
            else
            {
                tcontent = await GetContentAsync(player.UserInfo.Identifier, null, true);
            }
            return tcontent;
        }

        public new async Task<TContent> GetContentAsync(int playerIdentifier, Player player = null, bool isCreatingNew = true)
        {
            if (getContentTasks.TryGetValue(playerIdentifier, out var value))
            {
                await value;
            }
            if (!contents.TryGetValue(playerIdentifier, out var content) && isCreatingNew)
            {
                content = new TContent();
                TaskCompletionSource<bool> readTask = new TaskCompletionSource<bool>();
                getContentTasks.Add(playerIdentifier, readTask.Task);
                try
                {
                    IAltaFile file = await PlayerDataFileHelper<TSaveFormat>.Instance.GetFileAsync(playerIdentifier);
                    await content.LoadAsync(playerIdentifier, file);
                    contents[playerIdentifier] = content;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, ex.ToString());
                }
                readTask.SetResult(result: true);
                getContentTasks.Remove(playerIdentifier);
            }
            return content;
        }
    }
}
