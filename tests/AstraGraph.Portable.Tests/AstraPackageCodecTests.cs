// SPDX-FileCopyrightText: 2026 Astro
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileComment: Community Funding Additional Permission applies; see COMMUNITY-FUNDING-PERMISSION.md.

using NUnit.Framework;

namespace Content.AstraGraph.Portable.Tests;

[TestFixture]
public sealed class AstraPackageCodecTests
{
    [Test]
    public void RoundTripPreservesIdentityAndBytecode()
    {
        var identity = new AstraPackageIdentity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AstraExecutionSide.Predicted,
            42,
            100,
            200);
        var source = new AstraPackage(identity, [1, 2, 3, 4]);

        Assert.That(AstraPackageCodec.TryEncode(source, out var encoded), Is.True);
        Assert.That(AstraPackageCodec.TryDecode(encoded, out var decoded), Is.True);
        Assert.That(decoded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(decoded!.Identity, Is.EqualTo(identity));
            Assert.That(decoded.Bytecode, Is.EqualTo(source.Bytecode));
        });
    }

    [Test]
    public void RejectsTruncatedAndOversizedPayloads()
    {
        Assert.That(AstraPackageCodec.TryDecode([1, 2, 3], out _), Is.False);
        var oversized = new AstraPackage(
            new AstraPackageIdentity(Guid.NewGuid(), Guid.NewGuid(), AstraExecutionSide.Client, 0, 0, 0),
            new byte[AstraPackageCodec.MaxBytecodeBytes + 1]);
        Assert.That(AstraPackageCodec.TryEncode(oversized, out _), Is.False);
    }

    [Test]
    public void ContentHashIsDeterministic()
    {
        byte[] data = [10, 20, 30];
        var first = AstraPackageCodec.ComputeContentHash(data);
        var second = AstraPackageCodec.ComputeContentHash(data);
        var different = AstraPackageCodec.ComputeContentHash([10, 20, 31]);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Is.Not.EqualTo(different));
    }
}
