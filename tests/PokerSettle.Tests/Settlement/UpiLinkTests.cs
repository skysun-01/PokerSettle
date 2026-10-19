using PokerSettle.Core.ViewModels.Games;

namespace PokerSettle.Tests.Settlement;

public class UpiLinkTests
{
    [Fact]
    public void Builds_a_upi_pay_link_with_escaped_fields_and_rupee_amount()
    {
        var link = UpiLink.Build(" rahul@okaxis ", "Rahul Sharma", 125_050, "Poker Sat, 24 Oct");

        Assert.Equal("upi://pay?pa=rahul%40okaxis&pn=Rahul%20Sharma&am=1250.50&cu=INR&tn=Poker%20Sat%2C%2024%20Oct", link);
    }

    [Fact]
    public void Long_notes_are_shortened() =>
        Assert.EndsWith("&tn=" + Uri.EscapeDataString(new string('x', 50)), UpiLink.Build("a@b", "A", 100, new string('x', 80)));
}
