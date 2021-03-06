﻿using System;
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
using SharpDX;
using SCommon;
using SCommon.Database;
//typedefs
//using TargetSelector = SCommon.TS.TargetSelector;

namespace SCommon.Orbwalking
{
    public class Orbwalker
    {
        public enum Mode
        {
            None,
            Combo,
            Mixed,
            LaneClear,
            LastHit,
        }

        private Random m_rnd;
        private int m_lastAATick;
        private int m_lastWindUpEndTick;
        private int m_lastWindUpTime;
        private int m_lastAttackCooldown;
        private int m_lastAttackCompletesAt;
        private int m_lastMoveTick;
        private int m_lastAttackTick;
        private float m_baseAttackSpeed;
        private float m_baseWindUp;
        private bool m_attackInProgress;
        private bool m_Attack;
        private bool m_Move;
        private Vector2 m_lastAttackPos;
        private Vector3 m_orbwalkingPoint;
        private ConfigMenu m_Configuration;
        private bool m_orbwalkEnabled;
        private AttackableUnit m_forcedTarget;
        private bool m_attackReset;
        private AttackableUnit m_lastTarget;
        private Obj_AI_Base m_towerTarget;
        private Obj_AI_Base m_sourceTower;
        private int m_towerAttackTick;
        private Func<bool> m_fnCanAttack;
        private Func<bool> m_fnCanMove;
        private Func<AttackableUnit, bool> m_fnCanOrbwalkTarget;
        private Func<bool> m_fnShouldWait;

        public Orbwalker(Menu menuToAttach)
        {
            m_rnd = new Random();
            m_lastAATick = 0;
            m_lastWindUpEndTick = 0;
            m_lastMoveTick = 0;
            m_Attack = true;
            m_Move = true;
            m_baseWindUp = 1f / (ObjectManager.Player.AttackCastDelay * ObjectManager.Player.GetAttackSpeed());
            m_baseAttackSpeed = 1f / (ObjectManager.Player.AttackDelay * ObjectManager.Player.GetAttackSpeed());
            m_orbwalkingPoint = Vector3.Zero;
            m_Configuration = new ConfigMenu(this, menuToAttach);
            m_orbwalkEnabled = true;
            m_forcedTarget = null;
            m_lastTarget = null;
            m_fnCanAttack = null;
            m_fnCanMove = null;
            m_fnCanOrbwalkTarget = null;
            m_fnShouldWait = null;

            Game.OnUpdate += Game_OnUpdate;
            Obj_AI_Base.OnProcessSpellCast += Obj_AI_Base_OnProcessSpellCast;
            
            Obj_AI_Base.OnSpellCast += Obj_AI_Base_OnDoCast;
            Obj_AI_Base.OnBuffGain += Obj_AI_Base_OnBuffGain;
            Obj_AI_Base.OnBuffLose += Obj_AI_Base_OnBuffLose;
            Obj_AI_Base.OnNewPath += Obj_AI_Base_OnNewPath;
            Obj_AI_Base.OnPlayAnimation += Obj_AI_Base_OnPlayAnimation;
            Spellbook.OnStopCast += Spellbook_OnStopCast;
        }

        private void Spellbook_OnStopCast(Obj_AI_Base sender, SpellbookStopCastEventArgs args)
        {
            if (sender.IsValid && sender.IsMe && args.DestroyMissile && args.StopAnimation)
                ResetAATimer();
        }

        private void Obj_AI_Base_OnBuffGain(Obj_AI_Base sender, Obj_AI_BaseBuffGainEventArgs args)
        {
            if (sender.IsMe)
            {
                string buffname = args.Buff.Name.ToLower();
                if (buffname == "jaycestancegun" || buffname == "jaycestancehammer" || buffname == "swainmetamorphism" || buffname == "gnartransform" || buffname == "rengarqbase" || buffname == "rengarqemp")
                    ResetOrbwalkValues();
            }
        }

        private void Obj_AI_Base_OnBuffLose(Obj_AI_Base sender, Obj_AI_BaseBuffLoseEventArgs args)
        {
            if (sender.IsMe)
            {
                string buffname = args.Buff.Name.ToLower();
                if (buffname == "swainmetamorphism" || buffname == "gnartransform")
                    ResetOrbwalkValues();
                else if (buffname == "rengarqbase" || buffname == "rengarqemp")
                {
                    if (m_baseAttackSpeed == 0.5f)
                        SetOrbwalkValues();
                }
            }
        }

        /// <summary>
        /// Gets Orbwalker's active mode
        /// </summary>
        public Mode ActiveMode
        {
            get
            {
                if (m_Configuration.Combo)
                    return Mode.Combo;

                if (m_Configuration.Harass)
                    return Mode.Mixed;

                if (m_Configuration.LaneClear)
                    return Mode.LaneClear;

                if (m_Configuration.LastHit)
                    return Mode.LastHit;

                return Mode.None;
            }
        }

        /// <summary>
        /// Gets Last Auto Attack Tick
        /// </summary>
        public int LastAATick
        {
            get { return m_lastAATick; }
        }

        /// <summary>
        /// Gets Last WindUp tick
        /// </summary>
        public int LastWindUpEndTick
        {
            get { return m_lastWindUpEndTick; }
        }

        /// <summary>
        /// Gets Last Movement tick
        /// </summary>
        public int LastMoveTick
        {
            get { return m_lastMoveTick; }
        }

        /// <summary>
        /// Gets Configuration menu;
        /// </summary>
        public ConfigMenu Configuration
        {
            get { return m_Configuration; }
        }

        /// <summary>
        /// Gets or sets orbwalking point
        /// </summary>
        public Vector3 OrbwalkingPoint
        {
            get { return m_orbwalkingPoint == Vector3.Zero ? Game.CursorPos : m_orbwalkingPoint; }
            set { m_orbwalkingPoint = value; }
        }

        /// <summary>
        /// Gets or sets orbwalking is enabled
        /// </summary>
        public bool Enabled
        {
            get { return m_orbwalkEnabled; }
            set { m_orbwalkEnabled = value; }
        }

        /// <summary>
        /// Gets or sets forced orbwalk target
        /// </summary>
        public AttackableUnit ForcedTarget
        {
            get { return m_forcedTarget; }
            set { m_forcedTarget = value; }
        }

        /// <summary>
        /// Gets base attack speed value
        /// </summary>
        public float BaseAttackSpeed
        {
            get { return m_baseAttackSpeed; }
        }

        /// <summary>
        /// Gets base windup value
        /// </summary>
        public float BaseWindup
        {
            get { return m_baseWindUp; }
        }

        /// <summary>
        /// Resets auto attack timer
        /// </summary>
        public void ResetAATimer()
        {
            if (m_baseAttackSpeed != 0.5f)
            {
                m_lastAATick = Core.GameTickCount - Game.Ping / 2 - m_lastAttackCooldown;
                m_lastAttackTick = 0;
                m_attackReset = true;
                m_attackInProgress = false;
            }
        }

        /// <summary>
        /// Resets orbwalk values
        /// </summary>
        public void ResetOrbwalkValues()
        {
            m_baseAttackSpeed = 0.5f;
        }
        
        /// <summary>
        /// Sets orbwalk value
        /// </summary>
        public void SetOrbwalkValues()
        {
            m_baseWindUp = 1f / (ObjectManager.Player.AttackCastDelay * ObjectManager.Player.GetAttackSpeed());
            m_baseAttackSpeed = 1f / (ObjectManager.Player.AttackDelay * ObjectManager.Player.GetAttackSpeed());
        }

        /// <summary>
        /// Checks if player can attack
        /// </summary>
        /// <returns>true if can attack</returns>
        public bool CanAttack(int t = 0)
        {
            if (!m_Attack)
                return false;

            if (m_attackReset)
                return true;

            if (m_fnCanAttack != null)
                return m_fnCanAttack();

            if (ObjectManager.Player.CharData.BaseSkinName == "Graves" && !ObjectManager.Player.HasBuff("GravesBasicAttackAmmo1") && !ObjectManager.Player.HasBuff("GravesBasicAttackAmmo2"))
                return false;

            return Core.GameTickCount + t + Game.Ping - m_lastAATick - m_Configuration.ExtraWindup - (m_Configuration.LegitMode && !ObjectManager.Player.IsMelee ? Math.Max(100, ObjectManager.Player.AttackDelay * 1000) : 0) * m_Configuration.LegitPercent / 100f >= 1000 / (ObjectManager.Player.GetAttackSpeed() * m_baseAttackSpeed);
        }

        /// <summary>
        /// Checks if player can move
        /// </summary>
        /// <returns>true if can move</returns>
        public bool CanMove(int t = 0)
        {
            if (!m_Move)
                return false;

            if (Core.GameTickCount - m_lastWindUpEndTick < (ObjectManager.Player.AttackDelay - ObjectManager.Player.AttackCastDelay) * 1000f + (Game.Ping <= 30 ? 30 : 0))
                return true;
            
            if (m_fnCanMove != null)
                return m_fnCanMove();

            if (Utility.IsNonCancelChamp(ObjectManager.Player.CharData.BaseSkinName))
                return Core.GameTickCount - m_lastMoveTick >= 70 + m_rnd.Next(0, Game.Ping);
            
            return Core.GameTickCount + t - 20 - m_lastAATick - m_Configuration.ExtraWindup - m_Configuration.MovementDelay >= 1000 / (ObjectManager.Player.GetAttackSpeed() * m_baseWindUp);
        }

        /// <summary>
        /// Checks if player can orbwalk given target
        /// </summary>
        /// <param name="target">Target</param>
        /// <returns>true if can orbwalk target</returns>
        public bool CanOrbwalkTarget(AttackableUnit target)
        {
            if (target == null)
                return false;

            if (m_fnCanOrbwalkTarget != null)
                return m_fnCanOrbwalkTarget(target);

            if (target.IsValidTarget())
            {
                if (target.Type == GameObjectType.AIHeroClient)
                {
                    AIHeroClient hero = target as AIHeroClient;
                    return ObjectManager.Player.Distance(hero.ServerPosition) - hero.BoundingRadius - hero.GetScalingRange() + 20 < Utility.GetAARange() || Utility.InAARange(hero);
                }
                else
                    return (target.Type != GameObjectType.obj_AI_Turret || m_Configuration.AttackStructures) && ObjectManager.Player.Distance(target.Position) - target.BoundingRadius + 20 < Utility.GetAARange();
            }
            return false;
        }

        /// <summary>
        /// Checks if player can orbwalk given target in custom range
        /// </summary>
        /// <param name="target">Target</param>
        /// <param name="range">Custom range</param>
        /// <returns>true if can orbwalk target</returns>
        public bool CanOrbwalkTarget(AttackableUnit target, float range)
        {
            if (target.IsValidTarget())
            {
                if (target.Type == GameObjectType.AIHeroClient)
                {
                    AIHeroClient hero = target as AIHeroClient;
                    return ObjectManager.Player.Distance(hero.ServerPosition) - hero.BoundingRadius - hero.GetScalingRange() + 10 < range + ObjectManager.Player.BoundingRadius + ObjectManager.Player.GetScalingRange();
                }
                else
                    return ObjectManager.Player.Distance(target.Position) - target.BoundingRadius + 20 < range + ObjectManager.Player.BoundingRadius + ObjectManager.Player.GetScalingRange();
            }
            return false;
        }

        /// <summary>
        /// Checks if player can orbwalk given target from custom position
        /// </summary>
        /// <param name="target">Target</param>
        /// <param name="position">Custom position</param>
        /// <returns>true if can orbwalk target</returns>
        public bool CanOrbwalkTarget(AttackableUnit target, Vector3 position)
        {
            if (target.IsValidTarget())
            {
                if (target.Type == GameObjectType.AIHeroClient)
                {
                    AIHeroClient hero = target as AIHeroClient;
                    return position.Distance(hero.ServerPosition) - hero.BoundingRadius - hero.GetScalingRange() < Utility.GetAARange();
                }
                else
                    return position.Distance(target.Position) - target.BoundingRadius < Utility.GetAARange();
            }
            return false;
        }

        /// <summary>
        /// Orbwalk itself
        /// </summary>
        /// <param name="target">Target</param>
        public void Orbwalk(AttackableUnit target)
        {
            Orbwalk(target, OrbwalkingPoint);
        }

        /// <summary>
        /// Orbwalk itself
        /// </summary>
        /// <param name="target">Target</param>
        /// <param name="point">Orbwalk point</param>
        public void Orbwalk(AttackableUnit target, Vector3 point)
        {
            if (!m_attackInProgress)
            {
                if (CanOrbwalkTarget(target))
                {
                    if (CanAttack())
                    {
                        BeforeAttackArgs args = Events.FireBeforeAttack(this, target);
                        if (args.Process)
                            Attack(target);
                        else
                        {
                            if(CanMove())
                            {
                                if (m_Configuration.DontMoveInRange && target.Type == GameObjectType.AIHeroClient)
                                    return;

                                if ((m_Configuration.LegitMode && !ObjectManager.Player.IsMelee) || !m_Configuration.LegitMode)
                                    Move(point);
                            }
                        }
                    }
                    else if (CanMove())
                    {
                        if (m_Configuration.DontMoveInRange && target.Type == GameObjectType.AIHeroClient)
                            return;

                        if ((m_Configuration.LegitMode && !ObjectManager.Player.IsMelee) || !m_Configuration.LegitMode)
                            Move(point);
                    }
                }
                else
                {
                    Move(point);
                }
            }
        }
       
        private float GetAnimationTime()
        {
            return 1f / (ObjectManager.Player.GetAttackSpeed() * m_baseAttackSpeed);
        }

        private float GetWindupTime()
        {
            return 1f / (ObjectManager.Player.GetAttackSpeed() * m_baseWindUp) + m_Configuration.ExtraWindup;
        }

        private void Move(Vector3 pos)
        {
            if (!m_attackInProgress && CanMove() && (!CanAttack(60) || CanAttack()))
            {
                Vector3 playerPos = ObjectManager.Player.ServerPosition;

                bool holdzone = m_Configuration.DontMoveMouseOver || m_Configuration.HoldAreaRadius != 0;
                var holdzoneRadiusSqr = Math.Max(m_Configuration.HoldAreaRadius * m_Configuration.HoldAreaRadius, ObjectManager.Player.BoundingRadius * ObjectManager.Player.BoundingRadius * 4);
                if (holdzone && playerPos.Distance(pos, true) < holdzoneRadiusSqr)
                {
                    if ((Core.GameTickCount + Game.Ping / 2 - m_lastAATick) * 0.6f >= 1000f / (ObjectManager.Player.GetAttackSpeed() * m_baseWindUp))
                        Player.IssueOrder(GameObjectOrder.Stop, playerPos);
                    m_lastMoveTick = Core.GameTickCount + m_rnd.Next(1, 20);
                    return;
                }

                /*expermential*/
                var t = GetTarget();
                if (ObjectManager.Player.IsMelee && CanOrbwalkTarget(t) && t.Type == GameObjectType.AIHeroClient && m_Configuration.MagnetMelee && ObjectManager.Player.Distance(t) - t.BoundingRadius < m_Configuration.StickRange)
                    return;
                /*expermential*/

                if (ObjectManager.Player.Distance(pos, true) < 22500)
                    pos = playerPos.Extend(pos, (m_rnd.NextFloat(0.6f, 1.01f) + 0.2f) * 400).To3D();


                if (m_lastMoveTick + 150 + Math.Min(60, Game.Ping) < Core.GameTickCount)
                {
                    Player.IssueOrder(GameObjectOrder.MoveTo, pos);
                    m_lastMoveTick = Core.GameTickCount + m_rnd.Next(1, 20);
                }
            }
        }
        

        private void Attack(AttackableUnit target)
        {
            if (m_lastAttackTick < Core.GameTickCount && !m_attackInProgress)
            {
                m_lastWindUpEndTick = 0;
                m_lastAttackTick = Core.GameTickCount + m_rnd.Next(1, 20);
                m_lastAATick = Core.GameTickCount + Game.Ping;
                m_attackInProgress = true;
                Player.IssueOrder(GameObjectOrder.AttackUnit, target);
            }
        }

        private void Magnet(AttackableUnit target)
        {
            if (!m_attackInProgress)
            {
                if (ObjectManager.Player.AttackRange <= m_Configuration.StickRange)
                {
                    if (!CanOrbwalkTarget(target) && target.IsValidTarget(m_Configuration.StickRange))
                    {
                        /*expermential*/
                        OrbwalkingPoint = target.Position.Extend(ObjectManager.Player.ServerPosition, -(m_rnd.NextFloat(0.6f, 1.01f) + 0.2f) * 400).To3D();
                        /*expermential*/
                    }
                    else
                        OrbwalkingPoint = Vector3.Zero;
                }
                else
                    OrbwalkingPoint = Vector3.Zero;
            }
            else
                OrbwalkingPoint = Vector3.Zero;
        }

        private Obj_AI_Base GetLaneClearTarget()
        {
            Obj_AI_Base unkillableMinion = null;
            foreach (var minion in EntityManager.MinionsAndMonsters.GetLaneMinions(EntityManager.UnitTeam.Enemy, Player.Instance.Position, ObjectManager.Player.AttackRange + 100f).OrderByDescending(p => ObjectManager.Player.GetAutoAttackDamage(p)))
            {
                float t = GetAnimationTime() + minion.Distance(ObjectManager.Player.ServerPosition) / Utility.GetProjectileSpeed();
                if (CanOrbwalkTarget(minion))
                {
                    if (minion.Health - Damage.Prediction.GetPrediction(minion, t * 1000f) > 2 * Damage.AutoAttack.GetDamage(minion, true) || Damage.Prediction.IsLastHitable(minion))
                    {
                        //check if minion is about to be attacked
                        if (Damage.Prediction.AggroCount(minion) == 0 && ObjectManager.Get<Obj_AI_Minion>().Any(p => p.IsEnemy && p.IsMinion && p.ServerPosition.Distance(minion.ServerPosition) < p.AttackRange - p.MoveSpeed * (ObjectManager.Player.AttackDelay * 2f) && p.Path.Length > 0))
                            continue;

                        return minion;
                    }
                    else
                    {
                        if (Damage.Prediction.GetPrediction(minion, t * 1000f) == 0 && Damage.Prediction.AggroCount(minion) == 0 && ObjectManager.Get<Obj_AI_Minion>().Any(p => p.IsEnemy && p.IsMinion && p.ServerPosition.Distance(minion.ServerPosition) < p.AttackRange - p.MoveSpeed * (ObjectManager.Player.AttackDelay * 2f) && p.Path.Length > 0))
                            unkillableMinion = minion;
                    }
                }
            }
            var mob = GetJungleClearTarget();
            if (mob != null)
                return mob;

            return unkillableMinion;
        }

        private Obj_AI_Base GetJungleClearTarget()
        {
            Obj_AI_Base mob = null;
            if (Game.MapId == GameMapId.SummonersRift || Game.MapId == GameMapId.TwistedTreeline)
            {
                int mobPrio = 0;
                foreach (var minion in EntityManager.MinionsAndMonsters.GetJungleMonsters(Player.Instance.Position,2000))
                {
                    if (CanOrbwalkTarget(minion))
                    {
                        int prio = minion.GetJunglePriority();
                        if (minion.Health < ObjectManager.Player.GetAutoAttackDamage(minion))
                            return minion;
                        else
                        {
                            if (mob == null)
                            {
                                mob = minion;
                                mobPrio = prio;
                            }
                            else if (prio < mobPrio)
                            {
                                mob = minion;
                                mobPrio = prio;
                            }
                        }
                    }
                }
            }
            return mob;
        }

        private Obj_AI_Base FindKillableMinion()
        {
            if (m_Configuration.SupportMode)
                return null;

            foreach (var minion in EntityManager.MinionsAndMonsters.GetLaneMinions(EntityManager.UnitTeam.Enemy,Player.Instance.Position,ObjectManager.Player.AttackRange + 100f).OrderByDescending(p => ObjectManager.Player.GetAutoAttackDamage(p)))
            {
                if (CanOrbwalkTarget(minion) && Damage.Prediction.IsLastHitable(minion))
                    return minion;
            }
            return null;
        }

        public bool ShouldWait()
        {
            if (m_towerTarget != null && m_towerTarget.IsValidTarget() && CanOrbwalkTarget(m_towerTarget) && !m_towerTarget.IsSiegeMinion())
                return true;

            var underTurret = ObjectManager.Get<Obj_AI_Turret>().FirstOrDefault(p => p.IsValidTarget(950, false, ObjectManager.Player.ServerPosition) && p.IsAlly);

            if (underTurret != null)
            {
                return ObjectManager.Get<Obj_AI_Minion>()
                    .Any(
                        minion => minion.IsValidTarget(950 * 2f) && minion.Team != GameObjectTeam.Neutral &&
                                  minion.IsMinion && !minion.IsSiegeMinion() &&
                                  underTurret.IsValidTarget(950f, false, minion.ServerPosition));
                
            }

            if (m_fnShouldWait != null)
                return m_fnShouldWait();

            return
                ObjectManager.Get<Obj_AI_Minion>()
                    .Any(
                        minion =>
                            (minion.IsValidTarget() && minion.Team != GameObjectTeam.Neutral &&
                            Utility.InAARange(minion) && minion.IsMinion &&
                            (minion.Health - Damage.Prediction.GetPrediction(minion, GetAnimationTime() * 1000f * 2f + GetWindupTime() * 1000f, true) <= Damage.AutoAttack.GetDamage(minion, true) * (int)(Math.Ceiling(Damage.Prediction.AggroCount(minion) / 2f)))));

        }


        public AttackableUnit GetTarget()
        {
            bool wait = false;
            if(ActiveMode == Mode.LaneClear)
                wait = ShouldWait();

            if (ActiveMode == Mode.LaneClear || ActiveMode == Mode.LastHit || ActiveMode == Mode.Mixed)
            {
                //turret farming
                if (m_towerTarget != null && m_sourceTower != null && m_sourceTower.IsValidTarget(float.MaxValue, false) && m_towerTarget.IsValidTarget() && CanOrbwalkTarget(m_towerTarget, ObjectManager.Player.AttackRange + 150f))
                {
                    float health = m_towerTarget.Health - Damage.Prediction.GetPrediction(m_towerTarget, (m_towerTarget.Distance(m_sourceTower.ServerPosition) / m_sourceTower.BasicAttack.MissileSpeed + m_sourceTower.AttackCastDelay) * 1000f);
                    if (Damage.Prediction.IsLastHitable(m_towerTarget))
                        return m_towerTarget;

                    if (m_towerTarget.Health - m_sourceTower.GetAutoAttackDamage(m_towerTarget) * 2f > 0)
                        return null;

                    else if (m_towerTarget.Health - m_sourceTower.GetAutoAttackDamage(m_towerTarget) > 0)
                    {
                        if (m_towerTarget.Health - m_sourceTower.GetAutoAttackDamage(m_towerTarget) - Damage.AutoAttack.GetDamage(m_towerTarget) <= 0)
                            return null;
                        else if (health - m_sourceTower.GetAutoAttackDamage(m_towerTarget) - Damage.AutoAttack.GetDamage(m_towerTarget) * 2f <= 0)
                            return m_towerTarget;
                    }

                    if (m_Configuration.FocusNormalWhileTurret)
                        return FindKillableMinion();

                    return null;
                }
                var killableMinion = FindKillableMinion();
                if (killableMinion != null)
                    return killableMinion;
            }

            if (m_forcedTarget != null && m_forcedTarget.IsValidTarget() && Utility.InAARange(m_forcedTarget))
                return m_forcedTarget;

            //buildings
            if (ActiveMode == Mode.LaneClear && m_Configuration.AttackStructures && !wait)
            {
                /* turrets */
                foreach (var turret in
                    ObjectManager.Get<Obj_AI_Turret>().Where(t => t.IsValidTarget() && Utility.InAARange(t)))
                {
                    return turret;
                }

                /* inhibitor */
                foreach (var turret in
                    ObjectManager.Get<Obj_BarracksDampener>().Where(t => t.IsValidTarget() && Utility.InAARange(t)))
                {
                    return turret;
                }

                /* nexus */
                foreach (var nexus in
                    ObjectManager.Get<Obj_HQ>().Where(t => t.IsValidTarget() && Utility.InAARange(t)))
                {
                    return nexus;
                }
            }

            //champions
            if (ActiveMode != Mode.LastHit)
            {
                if (ActiveMode == Mode.LaneClear && wait)
                    return null;

                if ((ActiveMode == Mode.LaneClear && !m_Configuration.DontAttackChampWhileLaneClear) || ActiveMode == Mode.Combo || ActiveMode == Mode.Mixed)
                {
                    float range = -1;
                    range = (ObjectManager.Player.IsMelee && m_Configuration.MagnetMelee && m_Configuration.StickRange > ObjectManager.Player.AttackRange) ? m_Configuration.StickRange : -1;
                    if (ObjectManager.Player.CharData.BaseSkinName == "Azir")
                        range = 950f;
                    var target = TargetSelector.GetTarget(range, DamageType.Physical);
                    if (target.IsValidTarget() && (Utility.InAARange(target) || (ActiveMode != Mode.LaneClear && ObjectManager.Player.IsMelee && m_Configuration.MagnetMelee && target.IsValidTarget(m_Configuration.StickRange))))
                        return target;
                }
            }

            if (!wait)
            {
                if (ActiveMode == Mode.LaneClear)
                {
                    var minion = GetLaneClearTarget();
                    if (minion != null)
                        return minion;
                }
            }
            return null;
        }

        public void RegisterCanAttack(Func<bool> fn)
        {
            m_fnCanAttack = fn;
        }

        public void RegisterCanMove(Func<bool> fn)
        {
            m_fnCanMove = fn;
        }

        public void RegisterCanOrbwalkTarget(Func<AttackableUnit, bool> fn)
        {
            m_fnCanOrbwalkTarget = fn;
        }

        public void RegisterShouldWait(Func<bool> fn)
        {
            m_fnShouldWait = fn;
        }

        public void UnRegisterCanAttack()
        {
            m_fnCanAttack = null;
        }

        public void UnRegisterCanMove()
        {
            m_fnCanMove = null;
        }

        public void UnRegisterCanOrbwalkTarget()
        {
            m_fnCanOrbwalkTarget = null;
        }

        public void UnRegisterShouldWait()
        {
            m_fnShouldWait = null;
        }

        private void Game_OnUpdate(EventArgs args)
        {
            if (ActiveMode == Mode.None || ObjectManager.Player.Spellbook.IsCastingSpell || ObjectManager.Player.IsDead)
                return;

            if (CanMove() && m_attackInProgress)
                m_attackInProgress = false;

            var m_lastTarget = GetTarget();

            if (ObjectManager.Player.IsMelee && m_Configuration.MagnetMelee && m_lastTarget is AIHeroClient)
                Magnet(m_lastTarget);
            else
                OrbwalkingPoint = Vector3.Zero;

            Orbwalk(m_lastTarget);
        }

        private void Obj_AI_Base_OnProcessSpellCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
        {
            if (sender.IsMe)
            {
                if (Utility.IsAutoAttack(args.SData.Name))
                {
                    OnAttackArgs onAttackArgs = Events.FireOnAttack(this, args.Target as AttackableUnit);
                    if (!onAttackArgs.Cancel)
                    {
                        m_lastAATick = Core.GameTickCount - Game.Ping / 2;
                        m_lastWindUpTime = (int)(sender.AttackCastDelay * 1000);
                        m_lastAttackCooldown = (int)(sender.AttackDelay * 1000);
                        m_lastAttackCompletesAt = m_lastAATick + m_lastWindUpTime;
                        m_lastAttackPos = ObjectManager.Player.ServerPosition.To2D();
                        m_attackInProgress = true;
                    }
                    if (m_baseAttackSpeed == 0.5f)
                        SetOrbwalkValues();
                }
                else if (Utility.IsAutoAttackReset(args.SData.Name))
                {
                    ResetAATimer();
                }
                else if (!Utility.IsAutoAttackReset(args.SData.Name))
                {
                    if (m_attackInProgress)
                        ResetAATimer();
                }
                else if (args.SData.Name == "AspectOfTheCougar")
                {
                    ResetOrbwalkValues();
                }
            }
            else
            {
                if (sender.Type == GameObjectType.obj_AI_Turret && args.Target.Type == GameObjectType.obj_AI_Minion && sender.Team == ObjectManager.Player.Team && args.Target.Position.Distance(ObjectManager.Player.ServerPosition) <= 2000)
                {
                    m_towerTarget = args.Target as Obj_AI_Base;
                    m_sourceTower = sender;
                    m_towerAttackTick = Core.GameTickCount - Game.Ping / 2;
                }
            }
        }

        private void Obj_AI_Base_OnNewPath(Obj_AI_Base sender, GameObjectNewPathEventArgs args)
        {
            if (sender.IsMe && args.IsDash && sender.CharData.BaseSkinName == "Rengar")
            {
                Events.FireOnAttack(this, m_lastTarget);
                m_lastAATick = Core.GameTickCount - Game.Ping / 2;
                m_lastWindUpTime = (int)(sender.AttackCastDelay * 1000);
                m_lastAttackCooldown = (int)(sender.AttackDelay * 1000);
                m_lastAttackCompletesAt = m_lastAATick + m_lastWindUpTime;
                m_lastAttackPos = ObjectManager.Player.ServerPosition.To2D();
                m_attackInProgress = true;
                if (m_baseAttackSpeed == 0.5f)
                    SetOrbwalkValues();

                Core.DelayAction(() =>
                {
                    m_lastWindUpEndTick = Core.GameTickCount;
                    m_attackInProgress = false;
                    Events.FireAfterAttack(this, m_lastTarget);
                }
                , (int)Math.Max(1, (args.Path.First().Distance(args.Path.Last()) / args.Speed * 1000)));
            }
        }

        private void Obj_AI_Base_OnDoCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
        {
            if (sender.IsMe)
            {
                if (Utility.IsAutoAttack(args.SData.Name))
                {
                    m_lastWindUpEndTick = Core.GameTickCount;
                    m_attackInProgress = false;
                    m_attackReset = false;
                    m_lastMoveTick = 0;
                    Events.FireAfterAttack(this, args.Target as AttackableUnit);
                }
            }
        }
        private void Obj_AI_Base_OnPlayAnimation(Obj_AI_Base sender, GameObjectPlayAnimationEventArgs args)
        {
            //if (sender.IsMe && m_attackInProgress && (args.Animation == "Run" || args.Animation == "Idle"))
            //{
            //    Game.PrintChat(args.Animation);
            //    ResetAATimer();
            //}
        }
    }
}
