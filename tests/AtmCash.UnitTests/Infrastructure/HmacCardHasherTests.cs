using AtmCash.Application.Common;
using AtmCash.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace AtmCash.UnitTests.Infrastructure;

public class HmacCardHasherTests
{
    private static HmacCardHasher Hasher(string pepper = "unit-test-pepper-0123456789-abcdefghij") =>
        new(Options.Create(new CardHashingOptions { Pepper = pepper }));

    [Fact]
    public void Same_card_gives_the_same_hash_and_the_card_number_is_not_in_it()
    {
        var hash = Hasher().Hash("4111 1111 1111 1111");

        hash.Should().Be(Hasher().Hash("4111111111111111"));
        hash.Should().HaveLength(64).And.NotContain("4111");
    }

    [Fact]
    public void Hash_depends_on_the_secret_pepper()
    {
        Hasher().Hash("4111111111111111").Should().NotBe(Hasher("another-pepper-0123456789-abcdefghijkl").Hash("4111111111111111"));
    }

    [Fact]
    public void Missing_pepper_fails_fast()
    {
        var act = () => Hasher(string.Empty);

        act.Should().Throw<InvalidOperationException>();
    }
}

public class BusinessDayTests
{
    [Fact]
    public void Day_starts_at_midnight_Istanbul_time()
    {
        // 2026-10-12 22:30 UTC is 2026-10-13 01:30 in Istanbul (UTC+3).
        var start = BusinessDay.StartOfDay(new DateTimeOffset(2026, 10, 12, 22, 30, 0, TimeSpan.Zero));

        start.Should().Be(new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero));
    }
}
