using System;
using MedWNetworkSim.Presentation;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class RelayCommandSafetyTests
{
    [Fact]
    public void Execute_CatchesThrownExceptions_AndReportsToSink()
    {
        var sink = new RecordingUiExceptionSink();
        var previousSink = UiExceptionBoundary.Sink;
        UiExceptionBoundary.Sink = sink;
        try
        {
            var command = new RelayCommand(() => throw new InvalidOperationException("boom"));

            var thrown = Record.Exception(() => command.Execute(null));

            Assert.Null(thrown);
            Assert.NotNull(sink.SafeMessage);
            Assert.Contains("failed", sink.SafeMessage!, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(sink.Exception);
            Assert.Equal("boom", sink.Exception!.Message);
        }
        finally
        {
            UiExceptionBoundary.Sink = previousSink;
        }
    }

    private sealed class RecordingUiExceptionSink : IUiExceptionSink
    {
        public string? SafeMessage { get; private set; }
        public Exception? Exception { get; private set; }

        public void ReportUiException(string safeMessage, Exception exception)
        {
            SafeMessage = safeMessage;
            Exception = exception;
        }
    }
}
