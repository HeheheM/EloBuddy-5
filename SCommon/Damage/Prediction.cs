﻿using System;
using System.Collections.Generic;
using System.Linq;
using EloBuddy;
using EloBuddy.SDK;
using EloBuddy.SDK.Utils;
using EloBuddy.SDK.ThirdParty;
using EloBuddy.SDK.Rendering;
using EloBuddy.SDK.Menu;
using EloBuddy.SDK.Menu.Values;
using EloBuddy.SDK.Events;
using EloBuddy.SDK.Enumerations;
using EloBuddy.SDK.Constants;

namespace SCommon.Damage
{
    public static class Prediction
    {
        private static readonly Dictionary<int, PredictedDamage> ActiveAttacks;
        private static Random s_Rnd;
        static Prediction()
        {
            ActiveAttacks = new Dictionary<int, PredictedDamage>();
            s_Rnd = new Random();
            Obj_AI_Base.OnProcessSpellCast += Obj_AI_Base_OnProcessSpellCast;
            Obj_AI_Base.OnSpellCast += Obj_AI_Base_OnDoCast;
            AttackableUnit.OnDamage += Obj_AI_Base_OnDamage;
            Obj_AI_Base.OnPlayAnimation += Obj_AI_Base_OnPlayAnimation;
            Game.OnUpdate += Game_OnUpdate;
        }
       
        /// <summary>
        /// Checks if given unit is last hitable
        /// </summary>
        /// <param name="unit">Unit</param>
        /// <returns>true if last hitable</returns>
        public static bool IsLastHitable(Obj_AI_Base unit, float extraWindup = 0)
        {
            float health = unit.Health - GetPrediction(unit, (unit.ServerPosition.To2D().Distance(ObjectManager.Player.ServerPosition.To2D()) / Orbwalking.Utility.GetProjectileSpeed() + ObjectManager.Player.AttackCastDelay) * 1000f, true);
            return health < AutoAttack.GetDamage(unit, true);
        }

        /// <summary>
        /// Gets damage prediction to given unit
        /// </summary>
        /// <param name="unit">Unit</param>
        /// <param name="t">t in ms</param>
        /// <returns></returns>
        public static float GetPrediction(Obj_AI_Base unit, float t, bool checkSeq = false)
        {

            float dmg = 0.0f;
            foreach (var attack in ActiveAttacks.Values)
            {
                if (attack.Source.IsValidTarget(float.MaxValue, false) && attack.Target.IsValidTarget(float.MaxValue, false))
                {
                    if (attack.Target.NetworkId == unit.NetworkId)
                    {
                        float d = attack.Target.Distance(attack.Source.ServerPosition);
                        float maxTravelTime = d / attack.ProjectileSpeed * 1000f;
                        if (!attack.Damaged)
                        {
                            if ((attack.Source.IsMelee && !attack.Processed) || !attack.Source.IsMelee)
                            {
                                float arriveTime = (attack.StartTick + attack.Delay + maxTravelTime) - Core.GameTickCount;
                                if (arriveTime < t && arriveTime > 0) //if minion's missile arrives earlier than me
                                    dmg += attack.Damage; //add minion's dmg

                                t -= attack.AnimationTime;

                                if (checkSeq)
                                {
                                    int seqAttacks = (int)Math.Floor(t / attack.AnimationTime);
                                    for (int i = 1; i < seqAttacks; i++)
                                    {
                                        arriveTime = attack.Delay + maxTravelTime;
                                        if (arriveTime < t)
                                        {
                                            dmg += attack.Damage;
                                            t -= attack.AnimationTime;
                                        }
                                        else
                                            break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            float elapsedTick = Core.GameTickCount - attack.StartTick;
                            if (attack.AnimationTime - elapsedTick > 0)
                            {
                                float arriveTime = attack.AnimationTime - elapsedTick + attack.Delay + maxTravelTime;
                                if (arriveTime < t)
                                    dmg += attack.Damage;

                                t -= attack.AnimationTime + (attack.AnimationTime - elapsedTick);

                                if (checkSeq)
                                {
                                    int seqAttacks = (int)Math.Floor(t / attack.AnimationTime);
                                    for (int i = 1; i < seqAttacks; i++)
                                    {
                                        arriveTime = attack.Delay + maxTravelTime;
                                        if (arriveTime < t)
                                        {
                                            dmg += attack.Damage;
                                            t -= attack.AnimationTime;
                                        }
                                        else
                                            break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (dmg < 0)
                dmg = 0;
            return dmg;
        }

        public static int AggroCount(Obj_AI_Base unit)
        {
            return ActiveAttacks.Values.Count(p => p != null && p.Target != null && p.Target.NetworkId == unit.NetworkId);
        }

        private static void Obj_AI_Base_OnProcessSpellCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
        {
            if (!sender.IsValidTarget(3000, false) || sender.Team != ObjectManager.Player.Team || sender is AIHeroClient || !Orbwalking.Utility.IsAutoAttack(args.SData.Name) || !(args.Target is Obj_AI_Base) || sender.Type == GameObjectType.obj_AI_Turret)
                return;

            var target = (Obj_AI_Base)args.Target;
            if(ActiveAttacks.ContainsKey(sender.NetworkId))
                ActiveAttacks.Remove(sender.NetworkId);

            var attackData = new PredictedDamage(
                sender,
                target,
                Core.GameTickCount - Game.Ping / 2,
                sender.AttackCastDelay * 1000f,
                sender.AttackDelay * 1000f,
                sender.IsMelee ? float.MaxValue : args.SData.MissileSpeed,
                (float)sender.GetAutoAttackDamage(target));
            ActiveAttacks.Add(sender.NetworkId, attackData);
        }

        private static void Obj_AI_Base_OnDoCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
        {
            if (ActiveAttacks.ContainsKey(sender.NetworkId))
                ActiveAttacks[sender.NetworkId].Processed = true;
        }

        private static void Obj_AI_Base_OnDamage(AttackableUnit sender, AttackableUnitDamageEventArgs args)
        {
            if (ActiveAttacks.ContainsKey(args.Target.NetworkId))
                ActiveAttacks[args.Target.NetworkId].Damaged = true;
        }

        private static void Obj_AI_Base_OnPlayAnimation(Obj_AI_Base sender, GameObjectPlayAnimationEventArgs args)
        {
            if (args.Animation == "Death" && ActiveAttacks.ContainsKey(sender.NetworkId))
                ActiveAttacks.Remove(sender.NetworkId);
        }

        private static void Game_OnUpdate(EventArgs args)
        {
            ActiveAttacks.ToList()
                .Where(pair => pair.Value.StartTick < Core.GameTickCount - 3000)
                .ToList()
                .ForEach(pair => ActiveAttacks.Remove(pair.Key));
        }

        #region predicted damage class
        /// <summary>
        /// Represetns predicted damage.
        /// </summary>
        public class PredictedDamage
        {
            /// <summary>
            /// The animation time
            /// </summary>
            public readonly float AnimationTime;

            /// <summary>
            /// Gets or sets the damage.
            /// </summary>
            /// <value>
            /// The damage.
            /// </value>
            public float Damage { get; private set; }

            /// <summary>
            /// Gets or sets the delay.
            /// </summary>
            /// <value>
            /// The delay.
            /// </value>
            public float Delay { get; private set; }

            /// <summary>
            /// Gets or sets the projectile speed.
            /// </summary>
            /// <value>
            /// The projectile speed.
            /// </value>
            public float ProjectileSpeed { get; private set; }

            /// <summary>
            /// Gets or sets the source.
            /// </summary>
            /// <value>
            /// The source.
            /// </value>
            public Obj_AI_Base Source { get; private set; }

            /// <summary>
            /// Gets or sets the start tick.
            /// </summary>
            /// <value>
            /// The start tick.
            /// </value>
            public int StartTick { get; internal set; }

            /// <summary>
            /// Gets or sets the target.
            /// </summary>
            /// <value>
            /// The target.
            /// </value>
            public Obj_AI_Base Target { get; private set; }

            /// <summary>
            /// Gets or sets a value indicating whether this <see cref="PredictedDamage"/> is processed.
            /// </summary>
            /// <value>
            ///   <c>true</c> if processed; otherwise, <c>false</c>.
            /// </value>
            public bool Processed { get; internal set; }

            public bool Damaged { get; internal set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="PredictedDamage"/> class.
            /// </summary>
            /// <param name="source">The source.</param>
            /// <param name="target">The target.</param>
            /// <param name="startTick">The start tick.</param>
            /// <param name="delay">The delay.</param>
            /// <param name="animationTime">The animation time.</param>
            /// <param name="projectileSpeed">The projectile speed.</param>
            /// <param name="damage">The damage.</param>
            public PredictedDamage(Obj_AI_Base source,
                Obj_AI_Base target,
                int startTick,
                float delay,
                float animationTime,
                float projectileSpeed,
                float damage)
            {
                Source = source;
                Target = target;
                StartTick = startTick;
                Delay = delay;
                ProjectileSpeed = projectileSpeed;
                Damage = damage;
                AnimationTime = animationTime;
                Damaged = false;
            }
        }
        #endregion
    }
}
