using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using static GTA.WeaponHash;

namespace UltimatePlayerRagdoll
{
    internal class DebugDrawObject
    {
        public enum ObjectType
        {
            None,
            Marker,
            Line
        }

        private int lifespan;
        private Vector3 pos, dest;
        private ObjectType type;
        private Color color;

        public DebugDrawObject(int lifespan, Vector3 pos, Color color)
        {
            this.lifespan = lifespan;
            this.pos = pos;
            type = ObjectType.Marker;
            this.color = color;
        }

        public DebugDrawObject(int lifespan, Vector3 pos, Vector3 dest, Color color)
        {
            this.lifespan = lifespan;
            this.pos = pos;
            this.dest = dest;
            type = ObjectType.Line;
            this.color = color;
        }

        public bool Draw()
        {
            if (type == ObjectType.Line)
            {
                World.DrawLine(pos, dest, color);
            }
            else if (type == ObjectType.Marker)
            {
                var scale = new Vector3(0.2f, 0.2f, 0.2f);
                World.DrawMarker(MarkerType.DebugSphere, dest, Vector3.Zero, Vector3.Zero, scale, color,
                    false, true);
            }

            lifespan -= 1;
            return lifespan > 0;
        }
    }

    public class ScriptMain : Script
    {
        private bool deadRagdollEnabled;
        private bool bulletImpactEnabled;
        private bool disableAtMission;

        private int debugTimer;

        private bool playerDead;
        private float previousHealth;
        private Random random;
        String ini = @".\scripts\UltimatePlayerRagdoll.ini";
        ScriptSettings iniset;
        private int noControlTimer;
        private List<DebugDrawObject> DebugDrawObjects = new List<DebugDrawObject>();
        private Dictionary<Ped, float> prevPedHealth = new Dictionary<Ped, float>();

        public ScriptMain()
        {
            Tick += OnTick;
            KeyDown += OnKeyDown;
            playerDead = false;
            previousHealth = Game.Player.Character.HealthFloat;
            random = new Random();
            if (!File.Exists(ini))
            {
                File.Create(ini).Close();
                iniset = ScriptSettings.Load(ini);
                iniset.SetValue("Settings", "deadRagdollEnabled", true);
                iniset.SetValue("Settings", "bulletImpactEnabled", true);
                iniset.SetValue("Settings", "disableAtMission", true);
                iniset.Save();
            }

            iniset = ScriptSettings.Load(ini);
            deadRagdollEnabled = iniset.GetValue("Settings", "deadRagdollEnabled", true);
            bulletImpactEnabled = iniset.GetValue("Settings", "bulletImpactEnabled", true);
            disableAtMission = iniset.GetValue("Settings", "disableAtMission", true);
            Game.TimeScale = 1;
            GTA.UI.Screen.ShowSubtitle("Ultimate Player Ragdoll mod loaded. Version 0.0.1");
        }

        void OnTick(Object sender, EventArgs e)
        {
            if (debugTimer > 0)
            {
                debugTimer -= 1;
                if (debugTimer == 0)
                {
                    Game.TimeScale = 1;
                }
            }

            List<DebugDrawObject> remove = new List<DebugDrawObject>();
            foreach (var o in DebugDrawObjects)
            {
                if (!o.Draw())
                {
                    remove.Add(o);
                }
            }

            foreach (var o in remove)
            {
                DebugDrawObjects.Remove(o);
            }

            Game.Player.Character.DiesOnLowHealth = false;
            if (!playerDead)
            {
                Game.Player.Character.FatalInjuryHealthThreshold = -10000;
                Game.Player.Character.InjuryHealthThreshold = -10000;
            }
            else
            {
                DisplayHelpTextThisFrame("Press ~INPUT_ENTER~ to respawn.");
                if (!Game.Player.Character.IsRagdoll)
                {
                    Game.Player.Character.Ragdoll();
                }
            }

            float health = Game.Player.Character.HealthFloat;
            // ReSharper disable once SpecifyACultureInStringConversionExplicitly
            //GTA.UI.Screen.ShowHelpTextThisFrame(health.ToString(), false);
            if (Game.Player.Character.Health < 100 && !playerDead)
            {
                //Game.Player.Character.Kill();
                Game.Player.Character.HealthFloat = 100.0f;
                //Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, Game.Player.Handle, 1.0f);
                //Function.Call(Hash._SET_PLAYER_HEALTH_RECHARGE_LIMIT, Game.Player.Handle, 150.0f);
                Notification.Show("You died.");
                Game.Player.Character.Ragdoll();
                //Game.Player.CanControlCharacter = false;
                Function.Call(Hash.SET_PLAYER_CONTROL, Game.Player, false,
                    SetPlayerControlFlag.SPC_LEAVE_CAMERA_CONTROL_ON);
                Game.Player.IgnoredByEveryone = true;
                Game.Player.WantedLevel = 0;
                playerDead = true;
            }

            if (Game.Player.Character.Health > 100 && playerDead)
            {
                playerDead = false;
                Game.Player.IgnoredByEveryone = false;
                Wait(100);
                Game.Player.Character.CancelRagdoll();
                Game.Player.CanControlCharacter = true;
            }
            //GTA.UI.Screen.ShowSubtitle($"Dead: {playerDead}, Health: {health}, isDead: {Game.Player.IsDead}", 1);

            foreach (var ped in World.GetNearbyPeds(Game.Player.Character, 1000))
            {
                if (prevPedHealth.TryGetValue(ped, out var prev))
                {
                    //Notification.Show("Ped found");
                    if (prev > ped.HealthFloat)
                    {
                        ApplyDamageToPed(ped, prev - ped.HealthFloat);
                        //Notification.Show("Damage dealt to Ped");
                    }
                    prevPedHealth[ped] = ped.HealthFloat;
                }
                else
                {
                    //Notification.Show("Added Ped to list");
                    prevPedHealth.Add(ped, ped.HealthFloat);
                }
            }
            if (health < previousHealth)
            {
                float damage = previousHealth - health;

                // if player was shot
                ApplyDamageToPed(Game.Player.Character, damage);

                // bulletImpact for inside vehicles
                if (noControlTimer > 0)
                {
                    noControlTimer -= 1;
                    if (noControlTimer == 0)
                    {
                        if (!playerDead)
                        {
                            Game.Player.CanControlCharacter = true;
                        }
                    }
                }

                if (Game.Player.Character.IsInVehicle())
                {
                    GTA.UI.Screen.StartEffect(ScreenEffect.MinigameTransitionOut, 1000);
                    Game.Player.CanControlCharacter = false;
                    noControlTimer += (int)(damage * 1.3f);
                }
            }

            var outArg = new OutputArgument();
            if (Function.Call<bool>(Hash.GET_PED_LAST_WEAPON_IMPACT_COORD, Game.Player.Character, outArg))
            {
                Vector3 loc = outArg.GetResult<Vector3>();
                var scale = new Vector3(0.2f, 0.2f, 0.2f);
                World.DrawMarker(MarkerType.DebugSphere, loc, Vector3.Zero, Vector3.Zero, scale, Color.Red, false,
                    true);
            }

            previousHealth = health;
        }

        private void ApplyDamageToPed(Ped p, float damage)
        {
            var health = p.HealthFloat;
            if (p.DamageRecords.ToArray().Length == 0) return;
            var lastDamageSource = p.DamageRecords.Last();
            WeaponHash[] ranged =
            {
                CombatShotgun, SniperRifle, VintagePistol, CombatPDW, HeavySniperMk2, HeavySniper, SweeperShotgun,
                MicroSMG, Pistol, PumpShotgun, APPistol, CeramicPistol, SMG, AssaultrifleMk2, HeavyShotgun, Minigun,
                UnholyHellbringer, PumpShotgunMk2, PericoPistol, CombatPistol, Gusenberg, CompactRifle,
                MarksmanRifleMk2, PrecisionRifle, SawnOffShotgun, SMGMk2, BullpupRifle, CombatMG, CarbineRifle,
                BullpupRifleMk2, SNSPistolMk2, NavyRevolver, SpecialCarbineMk2, DoubleActionRevolver, Pistol50, MG,
                MilitaryRifle, BullpupShotgun, Musket, AdvancedRifle, Widowmaker, MiniSMG, SNSPistol, PistolMk2,
                AssaultRifle, SpecialCarbine, Revolver, MarksmanRifle, HeavyRifle, RevolverMk2, ServiceCarbine,
                HeavyPistol, MachinePistol, CombatMGMk2, MarksmanPistol, AssaultShotgun, DoubleBarrelShotgun,
                AssaultSMG, CarbineRifleMk2
            };

            var randMult = random.Next(9, 14) / 10f;
            var lowHealthMult = 1f;
            if (health < 115)
            {
                lowHealthMult = 1.2f;
            }
            else if (health < 130)
            {
                lowHealthMult = 1.15f;
            }
            else if (health < 150)
            {
                lowHealthMult = 1.075f;
            }
            else if (health < 175)
            {
                lowHealthMult = 1.05f;
            }

            if (ranged.Contains(lastDamageSource.WeaponHash) && lastDamageSource.Attacker != null &&
                lastDamageSource.Attacker.Bones != null)
            {
                var attacker = lastDamageSource.Attacker;
                var res = attacker.Bones.Where(((bone) => bone.Index == 65)).ToArray();
                if (res.Length <= 0) return;
                var attackerHand = res.First().Position;
                DebugDrawObjects.Add(new DebugDrawObject(150, attackerHand, Color.GreenYellow));
                var outArg2 = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_PED_LAST_WEAPON_IMPACT_COORD, attacker, outArg2)) return;
                Vector3 loc = outArg2.GetResult<Vector3>();
                DebugDrawObjects.Add(new DebugDrawObject(150, loc, Color.Red));
                DebugDrawObjects.Add(new DebugDrawObject(150, loc, attackerHand, Color.Red));
                //Game.Player.CanControlRagdoll = true;
                var power = Math.Min(damage, 30f) / 2.5f * randMult * lowHealthMult;
                var time = (int)(damage * 20 * randMult * lowHealthMult) - 15;
                // if power is strong enough, apply ragdoll
                if (!(power + 3 * randMult > 7)) return;
                debugTimer += 150;
                Game.TimeScale = 0.15f;
                var offset = p.Position - loc;
                // ragdoll the ped and apply force from shot direction to hit position
                p.Ragdoll(time, RagdollType.Balance);
                var forceDirection = (loc - attackerHand).Normalized * power * 50;
                p.ApplyForce(forceDirection, offset);
                DebugDrawObjects.Add(new DebugDrawObject(150, loc, loc + forceDirection, Color.BlueViolet));
                //Notification.Show($"DBG: POWER: {power} TIME: {time}");
            }
            else
            {
                Notification.Show($"Damage was not caused by ranged weapon: {lastDamageSource.WeaponHash}");
            }
        }

        void OnKeyDown(Object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F && playerDead)
            {
                //playerDead = false;
                //Game.Player.Character.HealthFloat = 0f;
                Game.Player.Character.FatalInjuryHealthThreshold = 100;
                Game.Player.Character.InjuryHealthThreshold = 100;
                Game.Player.Character.Kill();
                Game.Player.Character.CancelRagdoll();
            }

            if (e.KeyCode == Keys.F8)
            {
                Game.Player.Character.HealthFloat = 300f;
                Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, Game.Player.Handle, 1.0f);
                Function.Call(Hash._SET_PLAYER_HEALTH_RECHARGE_LIMIT, Game.Player.Handle, 150.0f);
                playerDead = false;
                Game.Player.IgnoredByEveryone = false;
                Game.Player.Character.CancelRagdoll();
                Game.Player.CanControlCharacter = true;
            }
        }

        void DisplayHelpTextThisFrame(string text)
        {
            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_KEYBOARD_DISPLAY, text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP, 0, false, false, -1);
        }

        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        enum SetPlayerControlFlag
        {
            SPC_AMBIENT_SCRIPT = (1 << 1),
            SPC_CLEAR_TASKS = (1 << 2),
            SPC_REMOVE_FIRES = (1 << 3),
            SPC_REMOVE_EXPLOSIONS = (1 << 4),
            SPC_REMOVE_PROJECTILES = (1 << 5),
            SPC_DEACTIVATE_GADGETS = (1 << 6),
            SPC_REENABLE_CONTROL_ON_DEATH = (1 << 7),
            SPC_LEAVE_CAMERA_CONTROL_ON = (1 << 8),
            SPC_ALLOW_PLAYER_DAMAGE = (1 << 9),
            SPC_DONT_STOP_OTHER_CARS_AROUND_PLAYER = (1 << 10),
            SPC_PREVENT_EVERYBODY_BACKOFF = (1 << 11),
            SPC_ALLOW_PAD_SHAKE = (1 << 12)
        };
    }
}