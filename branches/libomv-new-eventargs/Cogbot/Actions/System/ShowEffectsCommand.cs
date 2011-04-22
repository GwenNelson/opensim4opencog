using System;
using OpenMetaverse;

namespace cogbot.Actions
{
    public class ShowEffectsCommand : Command,RegionMasterCommand
    {
        bool ShowEffects = false;
        bool registeredCallback = false;

        public ShowEffectsCommand(BotClient testClient)
        {
            Name = "showeffects";
            Description = "Prints out information for every viewer effect that is received. Usage: showeffects [on/off]";
            Category = CommandCategory.Other;
            Parameters = new [] {  new NamedParam(typeof(GridClient), null) };
        }
        public override CmdResult Execute(string[] args, UUID fromAgentID, OutputDelegate WriteLine)
        {
            if (args.Length == 0)
            {
                ShowEffects = true;
                return Success("Viewer effects will be shown on the console");
            }
            else if (args.Length == 1)
            {
                if (args[0] == "on")
                {
                    ShowEffects = true;
                    if (!registeredCallback)
                    {
                        registeredCallback = true;
                        Client.Avatars.OnEffect += new AvatarManager.EffectCallback(Avatars_OnEffect);
                        Client.Avatars.OnLookAt += new AvatarManager.LookAtCallback(Avatars_OnLookAt);
                        Client.Avatars.OnPointAt += Avatars_OnPointAt;
                    }
                    return Success("Viewer effects will be shown on the console");
                }
                else
                {
                    ShowEffects = false;
                    return Success("Viewer effects will not be shown");
                }
            }
            else
            {
                return ShowUsage();// " showeffects [on/off]";
            }
        }

        private void Avatars_OnPointAt(UUID sourceID, UUID targetID, Vector3d targetPos, 
            PointAtType pointType, float duration, UUID id)
        {
            if (ShowEffects)
                WriteLine(
                "ViewerEffect [PointAt]: SourceID: {0} TargetID: {1} TargetPos: {2} Type: {3} Duration: {4} ID: {5}",
                sourceID.ToString(), targetID.ToString(), targetPos, pointType, duration, 
                id.ToString());
        }

        private void Avatars_OnLookAt(UUID sourceID, UUID targetID, Vector3d targetPos, 
            LookAtType lookType, float duration, UUID id)
        {
            if (ShowEffects)
                WriteLine(
                "ViewerEffect [LookAt]: SourceID: {0} TargetID: {1} TargetPos: {2} Type: {3} Duration: {4} ID: {5}",
                sourceID.ToString(), targetID.ToString(), targetPos, lookType, duration,
                id.ToString());
        }

        private void Avatars_OnEffect(EffectType type, UUID sourceID, UUID targetID, 
            Vector3d targetPos, float duration, UUID id)
        {
            if (ShowEffects)
                WriteLine(
                "ViewerEffect [{0}]: SourceID: {1} TargetID: {2} TargetPos: {3} Duration: {4} ID: {5}",
                type, sourceID.ToString(), targetID.ToString(), targetPos, duration,
                id.ToString());
        }
    }
}