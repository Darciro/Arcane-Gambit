using ArcaneGambit.Rules;
using NUnit.Framework;

namespace ArcaneGambit.Tests
{
    public sealed class LockstepTests
    {
        static Command ToCommand(Ai.Choice c) => c.Kind switch
        {
            Ai.ChoiceKind.Recruit => Command.Recruit(c.Type, c.To),
            Ai.ChoiceKind.Move => new Command(CommandKind.Move, index: c.UnitId, to: c.To),
            Ai.ChoiceKind.Capture => new Command(CommandKind.Capture, index: c.UnitId, to: c.To),
            Ai.ChoiceKind.Card => Command.Play(c.Card, c.Picks),
            Ai.ChoiceKind.React => Command.React(c.Reaction),
            Ai.ChoiceKind.Foresight => Command.Foresight(c.Index),
            _ => Command.EndTurn(),
        };

        [TestCase(3)]
        [TestCase(41)]
        [TestCase(2026)]
        public void SameSeedAndCommands_KeepBothMatchesIdentical(int seed)
        {
            var host = Match.CreateDefault(seed);
            var guest = Match.CreateDefault(seed);
            var ais = new[] { new Ai(Side.White, seed), new Ai(Side.Black, seed + 1) };

            for (int step = 0; step < 600 && Lockstep.Actor(host) is Side actor; step++)
            {
                var command = ToCommand(ais[(int)actor].Plan(host));
                if (!command.Apply(host, actor))
                {
                    command = Command.EndTurn();
                    Assert.That(command.Apply(host, actor), Is.True, $"step {step}: no legal fallback");
                }
                Assert.That(command.Apply(guest, actor), Is.True, $"step {step}: {command} refused on the replica");
                Assert.That(Lockstep.Fingerprint(guest), Is.EqualTo(Lockstep.Fingerprint(host)), $"step {step}: {command}");
            }
            Assert.That(guest.Winner, Is.EqualTo(host.Winner));
        }

        [Test]
        public void Command_FromTheSideNotToAct_IsRefused()
        {
            var m = Match.CreateDefault(7);
            int before = Lockstep.Fingerprint(m);
            Assert.That(Command.EndTurn().Apply(m, Side.Black), Is.False);
            Assert.That(Lockstep.Fingerprint(m), Is.EqualTo(before));
            Assert.That(Command.EndTurn().Apply(m, Side.White), Is.True);
            Assert.That(Lockstep.Actor(m), Is.EqualTo(Side.Black));
        }

        [Test]
        public void Move_OfAnEnemyUnit_IsRefused()
        {
            var m = Match.CreateDefault(7);
            var enemy = m.At(Coord.Parse("d7"));
            Assert.That(new Command(CommandKind.Move, index: enemy.Id, to: Coord.Parse("d6")).Apply(m, Side.White), Is.False);
        }

        [Test]
        public void Fingerprint_ChangesWithTheState()
        {
            var m = Match.CreateDefault(7);
            int before = Lockstep.Fingerprint(m);
            m.EndTurn();
            Assert.That(Lockstep.Fingerprint(m), Is.Not.EqualTo(before));
        }
    }
}
