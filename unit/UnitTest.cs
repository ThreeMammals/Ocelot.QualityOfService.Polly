namespace Ocelot.QualityOfService.Polly.UnitTests;

public class UnitTest : Unit
{
    public override CancellationToken CancelMe => TestContext.Current.CancellationToken;
}
