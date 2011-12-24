using System;
using System.Collections.Generic;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.Packets;

namespace cogbot.Actions
{
    public class WhisperCommand : Command, BotPersonalCommand
    {
        public WhisperCommand(BotClient testClient)
        {
            Name = "whisper";
            Description = "Whisper something.";
            Category = CommandCategory.Communication;
        }

        public override CmdResult Execute(string[] args, UUID fromAgentID, OutputDelegate WriteLine)
        {
            int channel = 0;
            int startIndex = 0;
            string message = String.Empty;
            if (args.Length < 1)
            {
                return ShowUsage();// " whisper (optional channel) whatever";
            }
            else if (args.Length > 1 && (args[0].StartsWith("/") || args[0].StartsWith("#")))
            {
                if (Int32.TryParse(args[0].Substring(1), out channel))
                    startIndex = 1;
            }

            for (int i = startIndex; i < args.Length; i++)
            {
                message += args[i] + " ";
            }

            Client.Self.Chat(message, channel, ChatType.Whisper);

            return Success("Whispered " + message);
        }
    }
}