﻿using HUD;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Linq;

namespace RainMeadow
{
    public partial class RainMeadow
    {
        private void MeadowHooks()
        {
            MeadowCustomization.EnableCicada();
            MeadowCustomization.EnableLizard();

            On.RoomCamera.Update += RoomCamera_Update;

            IL.HUD.Map.ctor += Map_OwnerFixup;
            IL.HUD.Map.CreateDiscoveryTextureFromVisitedRooms += Map_OwnerFixup;

            On.RainWorldGame.AllowRainCounterToTick += RainWorldGame_AllowRainCounterToTick;
            On.ShelterDoor.Close += ShelterDoor_Close;
            On.OverWorld.LoadFirstWorld += OverWorld_LoadFirstWorld;
        }

        private void OverWorld_LoadFirstWorld(On.OverWorld.orig_LoadFirstWorld orig, OverWorld self)
        {
            orig(self);
            if (OnlineManager.lobby != null && OnlineManager.lobby.gameMode is MeadowGameMode)
            {
                self.activeWorld.rainCycle.timer = 800;
            }
        }

        private void ShelterDoor_Close(On.ShelterDoor.orig_Close orig, ShelterDoor self)
        {
            if (OnlineManager.lobby != null && OnlineManager.lobby.gameMode is MeadowGameMode)
            {
                return;
            }
            orig(self);
        }

        private bool RainWorldGame_AllowRainCounterToTick(On.RainWorldGame.orig_AllowRainCounterToTick orig, RainWorldGame self)
        {
            if(OnlineManager.lobby != null && OnlineManager.lobby.gameMode is MeadowGameMode)
            {
                return false;
            }
            return orig(self);
        }

        private void Map_OwnerFixup(ILContext il)
        {
            try
            {
                //else if (this.hud.owner.GetOwnerType() != HUD.OwnerType.RegionOverview)
                //{
                //    saveState = (this.hud.owner as SleepAndDeathScreen).saveState;
                //}
                // becomes
                //else if (this.hud.owner.GetOwnerType() == HUD.OwnerType.SleepScreen || this.hud.owner.GetOwnerType() == HUD.OwnerType.DeathScreen)
                //{
                //    if (this.hud.owner.GetOwnerType() == MeadowCustomization.creatureControllerHudOwner)
                //        this.hud.rainWorld.progression.currentSaveState;
                //    else 
                //        saveState = (this.hud.owner as SleepAndDeathScreen).saveState;
                //}

                var c = new ILCursor(il);
                var loc = il.Body.Variables.First(v=>v.VariableType.Name == "SaveState").Index;
                ILLabel vanilla = il.DefineLabel();
                ILLabel skipToEnd = null;
                MethodReference op_Ineq;
                c.GotoNext(moveType: MoveType.After,
                    i => i.MatchLdsfld<HUD.HUD.OwnerType>("RegionOverview"),
                    i => i.MatchCall(out op_Ineq),
                    i => i.MatchBrfalse(out skipToEnd)
                    );
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate((HUD.Map map) => map.hud.owner.GetOwnerType() != MeadowCustomization.CreatureController.controlledCreatureHudOwner);
                c.Emit(OpCodes.Brtrue, vanilla);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit<HUD.HudPart>(OpCodes.Ldfld, "hud");
                c.Emit<HUD.HUD>(OpCodes.Ldfld, "rainWorld");
                c.Emit<RainWorld>(OpCodes.Ldfld, "progression");
                c.Emit<PlayerProgression>(OpCodes.Ldfld, "currentSaveState");
                c.Emit(OpCodes.Stloc, loc);
                c.Emit(OpCodes.Br, skipToEnd);
                c.MarkLabel(vanilla);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }

        private void RoomCamera_Update(On.RoomCamera.orig_Update orig, RoomCamera self)
        {
            if(OnlineManager.lobby != null && OnlineManager.lobby.gameMode is MeadowGameMode meadowGameMode)
            {
                if(self.hud == null && self.followAbstractCreature?.realizedObject is Creature owner)
                {
                    if(owner != meadowGameMode.avatar.realizedCreature) { RainMeadow.Error($"Camera owner != avatar {owner} {meadowGameMode.avatar}"); }

                    self.hud = new HUD.HUD(new FContainer[]
                    {
                        self.ReturnFContainer("HUD"),
                        self.ReturnFContainer("HUD2")
                    }, self.room.game.rainWorld, owner is Player player? player : MeadowCustomization.creatureControllers.TryGetValue(owner.abstractCreature, out var controller) ? controller : throw new InvalidProgrammerException("Not player nor controlled creature"));

                    MeadowCustomization.InitMeadowHud(self);
                }
            }
            orig(self);
        }
    }
}
