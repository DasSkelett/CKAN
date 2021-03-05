using NUnit.Framework;

namespace Tests.Core.Registry
{
    [TestFixture]
    public class RegistryMultipleRepos
    {
        [Test]
        public void Empty()
        {
            CKAN.IRegistry registry = CKAN.MonolithicRegistry.Empty();
            Assert.IsInstanceOf<CKAN.MonolithicRegistry>(registry);
        }
    }
}

