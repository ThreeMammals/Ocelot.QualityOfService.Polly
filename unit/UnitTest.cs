namespace Ocelot.QualityOfService.Polly.UnitTests;

public class UnitTest : Unit
{
    public static readonly string NL = Environment.NewLine;
    public override CancellationToken CancelMe => TestContext.Current.CancellationToken;
}
