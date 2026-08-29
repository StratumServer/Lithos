namespace Lithos.Benchmarks;

internal interface IBenchmarkCase
{
    string Name { get; }

    string Description { get; }

    int OperationsPerIteration { get; }

    void Validate();

    int Run(int iterations);
}
