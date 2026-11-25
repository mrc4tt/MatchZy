using System.Collections.Generic;
using System.Linq;
using MatchZy;
using Xunit;

namespace MatchZy.Tests
{
    public class WeaponKillLogicTests
    {
        [Theory]
        [InlineData("awp", "awp")]
        [InlineData("weapon_awp", "awp")]
        [InlineData(" AK47 ", "ak47")]
        [InlineData("m4a1_silencer", "m4a1_silencer")]
        [InlineData("hegrenade", "hegrenade")]
        [InlineData("inferno", "inferno")]
        public void WeaponNameBecomesLowerCaseKeyWithoutPrefix(string weapon, string expected)
        {
            Assert.Equal(expected, WeaponKillLogic.Key(weapon));
        }

        [Theory]
        [InlineData("knife")]
        [InlineData("knife_t")]
        [InlineData("weapon_knife_karambit")]
        [InlineData("bayonet")]
        [InlineData("knife_m9_bayonet")]
        public void EveryKnifeModelCountsAsKnife(string weapon)
        {
            Assert.Equal(WeaponKillLogic.Knife, WeaponKillLogic.Key(weapon));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("world")]
        [InlineData("worldspawn")]
        public void KillWithoutWeaponIsNotCounted(string? weapon)
        {
            Assert.Null(WeaponKillLogic.Key(weapon));
        }

        [Theory]
        [InlineData("awp", "sniper")]
        [InlineData("ssg08", "sniper")]
        [InlineData("ak47", "rifle")]
        [InlineData("m4a1_silencer", "rifle")]
        [InlineData("usp_silencer", "pistol")]
        [InlineData("mp9", "smg")]
        [InlineData("mag7", "shotgun")]
        [InlineData("negev", "machinegun")]
        [InlineData("inferno", "grenade")]
        [InlineData("knife", "knife")]
        [InlineData("taser", "other")]
        [InlineData("something_new", "other")]
        public void WeaponHasAType(string key, string expected)
        {
            Assert.Equal(expected, WeaponKillLogic.TypeOf(key));
        }

        [Fact]
        public void TypeTotalsListEveryTypeAndAddUp()
        {
            var kills = new Dictionary<string, int> { ["ak47"] = 9, ["m4a1"] = 2, ["awp"] = 7, ["deagle"] = 3, ["knife"] = 1, ["taser"] = 1 };
            var totals = WeaponKillLogic.TypeTotals(kills);
            Assert.Equal(WeaponKillLogic.Types, totals.Keys.ToArray());
            Assert.Equal(11, totals["rifle"]);
            Assert.Equal(7, totals["sniper"]);
            Assert.Equal(3, totals["pistol"]);
            Assert.Equal(1, totals["knife"]);
            Assert.Equal(1, totals["other"]);
            Assert.Equal(0, totals["smg"]);
            Assert.Equal(kills.Values.Sum(), totals.Values.Sum());
        }

        [Fact]
        public void TypeTotalsOfNothingAreAllZero()
        {
            var totals = WeaponKillLogic.TypeTotals(null);
            Assert.Equal(WeaponKillLogic.Types.Length, totals.Count);
            Assert.All(totals.Values, v => Assert.Equal(0, v));
        }
    }
}
