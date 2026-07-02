namespace Fixtures.Opinionated;

// Regression #11: a method with no access modifier in a class is implicitly private.
// OP005 must NOT fire here — implicitly private methods are internal implementation,
// not API surface, and cannot be the flag-argument smell.
class ImplicitlyPrivateFixture
{
    void Helper(bool active)
    {
        Console.WriteLine(active);
    }
}
