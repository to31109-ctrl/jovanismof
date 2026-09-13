using System;
using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Game.Combat.EnemyDebuffs;
using Assets.Scripts.Game.Combat.EnemyDebuffs.Implementations;
using MegabonkTogether.Common.Persistence;

namespace MegabonkTogether.Persistence
{
    internal static class EnemyEffects
    {
        public static void Capture(Enemy enemy, SavedEnemy saved)
        {
            saved.EchoDamage = float.IsFinite(enemy.echoDamage) ? enemy.echoDamage : 0;
            if (enemy.debuffs == null) return;
            foreach (var kind in enemy.debuffs.Keys)
            {
                var effect = enemy.debuffs[kind];
                if (effect == null || effect.IsDone()) continue;
                DamageContainer damage = null;
                var fire = effect.TryCast<DebuffFire>();
                if (fire != null) damage = fire.dc;
                var mark = effect.TryCast<DebuffBloodmark>();
                if (mark != null) damage = mark.dc;
                saved.Debuffs.Add(new SavedDebuff
                {
                    Kind = (int)kind, Ticks = Math.Max(0, effect.ticksLeft), Stacks = Math.Max(0, effect.GetStacks()),
                    Damage = damage != null && float.IsFinite(damage.damage) ? damage.damage : 0,
                    Source = damage?.damageSource ?? "", Crit = damage?.crit ?? false,
                    Flags = damage == null ? 0 : (int)damage.flags,
                    ProcCoefficient = damage != null && float.IsFinite(damage.procCoefficient) ? damage.procCoefficient : 0,
                });
            }
        }

        public static void Apply(Enemy enemy, SavedEnemy saved)
        {
            enemy.echoDamage = saved.EchoDamage;
            foreach (var effect in saved.Debuffs)
            {
                var kind = (EDebuff)effect.Kind;
                var damage = new DamageContainer(effect.Damage, effect.Source)
                {
                    crit = effect.Crit, flags = (DcFlags)effect.Flags, procCoefficient = effect.ProcCoefficient,
                };
                var duration = effect.Ticks / (float)Math.Max(1, DebuffUtility.debuffTicksPerSecond);
                enemy.AddDebuffImplementation(new AddDebuffContainer(kind, damage, duration, effect.Stacks));
                if (enemy.debuffs != null && enemy.debuffs.TryGetValue(kind, out var live))
                    live._ticksLeft_k__BackingField = effect.Ticks;
                else
                    throw new InvalidOperationException($"Debuff {kind} was not restored");
            }
        }
    }
}
