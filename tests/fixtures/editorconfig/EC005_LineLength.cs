// EC005: line below exceeds 120 chars
namespace Fixtures
{
    class LongLine
    {
        string value = "This line is intentionally very long to trigger the max_line_length rule and it keeps going past the limit of 120";
    }
}
