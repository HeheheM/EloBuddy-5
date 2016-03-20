﻿using System;
using EloBuddy;
using EloBuddy.SDK.Events;
using EloBuddy.SDK;
using Color = System.Drawing.Color;

namespace RengarPro_Revamped
{
    class Program : Standarts

    {

        public static int LastQ, LastE, LastW, LastSpell;
        static void Main()
        {
            Loading.OnLoadingComplete += Loading_OnLoadingComplete;
        }

        private static void Loading_OnLoadingComplete(EventArgs args)
        {
            try
            {
            if (Rengar.Hero != Champion.Rengar)
            {
                return;
            }
            Game.OnUpdate += Game_OnUpdate;
            LaneClear.Initialize();
            JungleClear.Initialize();
            Menu.Init();
            Helper.Misc.Init();
            Helper.Targetting.Initialize();
            Orbwalker.OnPostAttack += Orbwalker_OnPostAttack;
            Orbwalker.OnPreAttack += Orbwalker_OnPreAttack;
            Obj_AI_Base.OnProcessSpellCast += Obj_AI_Base_OnProcessSpellCast;
            Dash.OnDash += Modes.Combo.Dash_OnDash;
            Drawing.OnDraw += Drawing_OnDraw;
            Chat.Print("RengarPro Revamped | Loaded !",Color.Blue);
            Chat.Print("RengarPro Revamped | Coded by Rexy",Color.BlueViolet);
            Chat.Print("RengarPro Revamped | Say Hello To New Renglet",Color.Crimson);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            }

        static void Game_OnUpdate(EventArgs args)
        {
            if (Orbwalker.ActiveModesFlags == Orbwalker.ActiveModes.Combo)
            {
                Modes.Combo.Do();
            }
        }
        private static void Obj_AI_Base_OnProcessSpellCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
        {
            try
            {
                if (sender.IsMe)
                {
                    switch (args.SData.Name.ToLower())
                    {
                        case "rengarq":
                            LastQ = Environment.TickCount;
                            LastSpell = Environment.TickCount;
                            Orbwalker.ResetAutoAttack();
                            break;

                        case "rengare":
                            LastE = Environment.TickCount;
                            LastSpell = Environment.TickCount;
                            if (Orbwalker.LastAutoAttack < Core.GameTickCount - Game.Ping / 2
                                && Core.GameTickCount - Game.Ping / 2
                                < Orbwalker.LastAutoAttack + Player.Instance.AttackDelay * 1000 + 40)
                            {
                                Orbwalker.ResetAutoAttack();
                            }
                            break;

                        case "rengarw":
                            LastW = Environment.TickCount;
                            LastSpell = Environment.TickCount;
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static void Orbwalker_OnPreAttack(AttackableUnit target, Orbwalker.PreAttackArgs args)
        {
            try
            {
                if (RengarHasUltimate)
                {
                    return;
                }
                if (Orbwalker.ActiveModesFlags.HasFlag(Orbwalker.ActiveModes.Combo) && !RengarHasPassive && Q.IsReady()
                        && !(Helper.MenuChecker.ComboModeSelected == 2 || Helper.MenuChecker.ComboModeSelected == 3 && Ferocity == 5))
                {
                    if (Player.Instance.Position.To2D().Distance(target.Position.To2D())
                       >= Player.Instance.BoundingRadius + Player.Instance.AttackRange + args.Target.BoundingRadius)
                    {
                        args.Process = false;
                        Q.Cast();

                    }
                }
            }
            catch (Exception e)
            {
                
                Console.WriteLine(e);
            }
        }

        private static void Orbwalker_OnPostAttack(AttackableUnit target, EventArgs args)
        {
            try
            {
                if (target.IsMe || !(target is AIHeroClient) || !target.IsValidTarget((Q.Range)) || RengarHasUltimate)
                {
                    return;
                }

                if (Orbwalker.ActiveModesFlags.HasFlag(Orbwalker.ActiveModes.Combo))
                {
                    Q.Cast();

                    Modes.Combo.CastItems();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static void Drawing_OnDraw(EventArgs args)
        {
            try
            {
                if (Helper.MenuChecker.DrawComboModeActive)
                {
                    switch (Helper.MenuChecker.ComboModeSelected)
                    {
                        case 1:
                            {
                                Drawing.DrawText(Drawing.Width * 0.70f, Drawing.Height * 0.95f, Color.White, "Mode : One Shot");
                                break;
                            }
                        case 2:
                            {
                                Drawing.DrawText(Drawing.Width * 0.70f, Drawing.Height * 0.95f, Color.White, "Mode : Snare");
                                break;
                            }
                        case 3:
                            {
                                Drawing.DrawText(Drawing.Width * 0.70f, Drawing.Height * 0.95f, Color.White, "Mode : AP Rengo");
                                break;
                            }
                    }
                }

                if (Helper.MenuChecker.DrawSelectedEnemyActive && TargetSelector.SelectedTarget.IsValidTarget() && TargetSelector.SelectedTarget.IsVisible && !TargetSelector.SelectedTarget.IsDead && !(TargetSelector.SelectedTarget.IsMinion || TargetSelector.SelectedTarget.IsMonster))
                {

                    Drawing.DrawText(
                    Drawing.WorldToScreen(TargetSelector.SelectedTarget.Position).X - 40,
                    Drawing.WorldToScreen(TargetSelector.SelectedTarget.Position).Y + 10,
                    Color.White,
                    "Selected Target");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
