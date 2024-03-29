using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.Assets;
using MushDLR223.ScriptEngines;

namespace Cogbot.Actions.Inventory
{
    public class CreateNotecardCommand : Command, BotPersonalCommand
    {
        private const int NOTECARD_CREATE_TIMEOUT = 1000*10;
        private const int NOTECARD_FETCH_TIMEOUT = 1000*10;
        private const int INVENTORY_FETCH_TIMEOUT = 1000*10;

        public CreateNotecardCommand(BotClient testClient)
        {
            Name = "createnotecard";
        }

        public override void MakeInfo()
        {
            Description = "Creates a notecard from a local text file and optionally embed an inventory item.";
            AddVersion(
                CreateParams(
                    "filename", typeof (string), "filename.txt file to read",
                    Optional("itemid", typeof (InventoryItem), " optionally embed an inventory item")));
						
            Category = CommandCategory.Inventory;
        }

        public override CmdResult ExecuteRequest(CmdRequest args)
        {
            UUID embedItemID = UUID.Zero, notecardItemID = UUID.Zero, notecardAssetID = UUID.Zero;
            string filename, fileData;
            bool success = false, finalUploadSuccess = false;
            string message = String.Empty;
            AutoResetEvent notecardEvent = new AutoResetEvent(false);

            if (args.Length == 1)
            {
                filename = args[0];
            }
            else if (args.Length == 2)
            {
                filename = args[0];
                UUID.TryParse(args[1], out embedItemID);
            }
            else
            {
                return ShowUsage(); // " createnotecard filename.txt";
            }

            if (!File.Exists(filename))
                return Failure("File \"" + filename + "\" does not exist");

            try
            {
                fileData = File.ReadAllText(filename);
            }
            catch (Exception ex)
            {
                return Failure("failed to open " + filename + ": " + ex.Message);
            }

            #region Notecard asset data

            AssetNotecard notecard = new AssetNotecard();
            notecard.BodyText = fileData;

            // Item embedding
            if (embedItemID != UUID.Zero)
            {
                // Try to fetch the inventory item
                InventoryItem item = FetchItem(embedItemID);
                if (item != null)
                {
                    notecard.EmbeddedItems = new List<InventoryItem> {item};
                    notecard.BodyText += (char) 0xdbc0 + (char) 0xdc00;
                }
                else
                {
                    return Failure("failed to fetch inventory item " + embedItemID);
                }
            }

            notecard.Encode();

            #endregion Notecard asset data

            Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(AssetType.Notecard),
                                               filename,
                                               filename + " created by OpenMetaverse BotClient " + DateTime.Now,
                                               AssetType.Notecard,
                                               UUID.Random(), InventoryType.Notecard, PermissionMask.All,
                                               delegate(bool createSuccess, InventoryItem item)
                                                   {
                                                       if (createSuccess)
                                                       {
                                                           #region Upload an empty notecard asset first

                                                           AutoResetEvent emptyNoteEvent = new AutoResetEvent(false);
                                                           AssetNotecard empty = new AssetNotecard();
                                                           empty.BodyText = "\n";
                                                           empty.Encode();

                                                           Client.Inventory.RequestUploadNotecardAsset(empty.AssetData,
                                                                                                       item.UUID,
                                                                                                       delegate(
                                                                                                           bool
                                                                                                           uploadSuccess,
                                                                                                           string status,
                                                                                                           UUID itemID,
                                                                                                           UUID assetID)
                                                                                                           {
                                                                                                               notecardItemID
                                                                                                                   =
                                                                                                                   itemID;
                                                                                                               notecardAssetID
                                                                                                                   =
                                                                                                                   assetID;
                                                                                                               success =
                                                                                                                   uploadSuccess;
                                                                                                               message =
                                                                                                                   status ??
                                                                                                                   "Unknown error uploading notecard asset";
                                                                                                               emptyNoteEvent
                                                                                                                   .Set();
                                                                                                           });

                                                           emptyNoteEvent.WaitOne(NOTECARD_CREATE_TIMEOUT, false);

                                                           #endregion Upload an empty notecard asset first

                                                           if (success)
                                                           {
                                                               // Upload the actual notecard asset
                                                               Client.Inventory.RequestUploadNotecardAsset(
                                                                   notecard.AssetData, item.UUID,
                                                                   delegate(bool uploadSuccess, string status,
                                                                            UUID itemID, UUID assetID)
                                                                       {
                                                                           notecardItemID = itemID;
                                                                           notecardAssetID = assetID;
                                                                           finalUploadSuccess = uploadSuccess;
                                                                           message = status ??
                                                                                     "Unknown error uploading notecard asset";
                                                                           notecardEvent.Set();
                                                                       });
                                                           }
                                                           else
                                                           {
                                                               notecardEvent.Set();
                                                           }
                                                       }
                                                       else
                                                       {
                                                           message = "Notecard item creation failed";
                                                           notecardEvent.Set();
                                                       }
                                                   }
                );

            notecardEvent.WaitOne(NOTECARD_CREATE_TIMEOUT, false);

            if (finalUploadSuccess)
            {
                WriteLine("Notecard successfully created, ItemID " + notecardItemID + " AssetID " + notecardAssetID,
                          Helpers.LogLevel.Info);
                return DownloadNotecard(notecardItemID, notecardAssetID);
            }
            else
                return Failure("Notecard creation failed: " + message);
        }

        private InventoryItem FetchItem(UUID itemID)
        {
            InventoryItem fetchItem = null;
            AutoResetEvent fetchItemEvent = new AutoResetEvent(false);

            EventHandler<ItemReceivedEventArgs> itemReceivedCallback =
                (s, e) =>
                    {
                        var item = e.Item;
                        if (item.UUID == itemID)
                        {
                            fetchItem = item;
                            fetchItemEvent.Set();
                        }
                    };

            Client.Inventory.ItemReceived += itemReceivedCallback;

            Client.Inventory.RequestFetchInventory(itemID, Client.Self.AgentID);

            fetchItemEvent.WaitOne(INVENTORY_FETCH_TIMEOUT, false);

            Client.Inventory.ItemReceived -= itemReceivedCallback;

            return fetchItem;
        }

        private CmdResult DownloadNotecard(UUID itemID, UUID assetID)
        {
            UUID transferID = UUID.Zero;
            AutoResetEvent assetDownloadEvent = new AutoResetEvent(false);
            byte[] notecardData = null;
            string error = "Timeout";

            Client.Assets.RequestInventoryAsset(assetID, itemID, UUID.Zero, Client.Self.AgentID, AssetType.Notecard,
                                                true,
                                                delegate(AssetDownload transfer, Asset asset)
                                                    {
                                                        if (transfer.Success)
                                                            notecardData = transfer.AssetData;
                                                        else
                                                            error = transfer.Status.ToString();
                                                        assetDownloadEvent.Set();
                                                    }
                );

            assetDownloadEvent.WaitOne(NOTECARD_FETCH_TIMEOUT, false);

            if (notecardData != null)
                return Success(Encoding.UTF8.GetString(notecardData));
            else
                return Failure("Error downloading notecard asset: " + error);
        }
    }
}