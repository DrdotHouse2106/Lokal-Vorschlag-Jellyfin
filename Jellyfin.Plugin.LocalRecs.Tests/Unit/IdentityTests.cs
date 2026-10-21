using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Api;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für den Identitätsnachweis (Sollwert unabhängig mit Python berechnet).
    /// </summary>
    public class IdentityTests
    {
        [Fact]
        public void Proof_IsHmacOfNonceServerIdAndHost_KeyedWithHashedToken()
        {
            var proof = IdentityController.Proof("token-123", new string('a', 0) + string.Concat(System.Linq.Enumerable.Repeat("ab", 32)), "c2b157ea3dc84584a30bcb96492eda16", "192.168.1.10:8096");

            proof.Should().Be("baadad25df095f9c875dbbf4fab4b8c6741d39c30d189a8c6ee490b7c57024dc");
        }

        [Fact]
        public void Proof_DependsOnHost_AndToken()
        {
            var nonce = string.Concat(System.Linq.Enumerable.Repeat("ab", 32));
            var proof = IdentityController.Proof("token-123", nonce, "s", "192.168.1.10:8096");

            IdentityController.Proof("token-123", nonce, "s", "jellyfin.example.org").Should().NotBe(proof);
            IdentityController.Proof("anderer", nonce, "s", "192.168.1.10:8096").Should().NotBe(proof);
        }

        [Fact]
        public void PadProofs_KnownAndUnknownDevicesLookAlike()
        {
            var real = IdentityController.Proof("token", new string('a', 64), "server", "host:8096");

            var unknown = IdentityController.PadProofs(System.Array.Empty<string>());
            var known = IdentityController.PadProofs(new[] { real });

            unknown.Should().HaveCount(IdentityController.MinProofs).And.OnlyContain(p => p.Length == 64);
            known.Should().HaveCount(IdentityController.MinProofs).And.Contain(real).And.OnlyContain(p => p.Length == 64);
        }
    }
}
