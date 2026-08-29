namespace Lithos.Benchmarks;

internal interface IBenchmarkCase
{
    string Name { get; }

    string Description { get; }

    void Validate();

    int Run(int iterations);
}
