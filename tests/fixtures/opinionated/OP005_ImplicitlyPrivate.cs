namespace Fixtures.Opinionated;

// Regression #11: a method with no access modifier in a class is implicitly private.
// OP005 must NOT fire here — implicitly private methods are internal implementation,
// not API surface, and cannot be the flag-argument smell.
class ImplicitlyPrivateFixture
{
    // No other findings may fire in this fixture: the harness's check_absent greps the raw
    // output for "OP005", and any finding at all echoes this OP005-named file path into it.
    void Helper(bool active)
    {
        _ = active;
    }
}
