using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Ansight;

/// <summary>
/// Displays the recent Ansight events as a list inside the slide-in sheet.
/// </summary>
public partial class EventsView : ContentView
{
    private const int MaxEvents = 50;
    private IDataSink? dataSink;

    public ObservableCollection<AppEventDisplay> VisibleEvents { get; } = new();

    public EventsView()
    {
        InitializeComponent();
        BindingContext = this;
    }

    public static readonly BindableProperty DataSinkProperty = BindableProperty.Create(nameof(DataSink),
                                                                                       typeof(IDataSink),
                                                                                       typeof(EventsView),
                                                                                       null,
                                                                                       propertyChanged: OnDataSinkChanged);

    public IDataSink DataSink
    {
        get => (IDataSink)GetValue(DataSinkProperty);
        set => SetValue(DataSinkProperty, value);
    }

    private static void OnDataSinkChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is EventsView view)
        {
            if (oldValue is IDataSink oldSink)
            {
                view.Unsubscribe(oldSink);
            }

            if (newValue is IDataSink newSink)
            {
                view.Subscribe(newSink);
            }
        }
    }

    private void Subscribe(IDataSink? sink)
    {
        dataSink = sink;
        if (sink != null)
        {
            sink.OnEventsUpdated -= HandleEventsUpdated;
            sink.OnEventsUpdated += HandleEventsUpdated;
        }

        RefreshEvents();
    }

    private void Unsubscribe(IDataSink? sink)
    {
        if (sink != null)
        {
            sink.OnEventsUpdated -= HandleEventsUpdated;
        }

        if (ReferenceEquals(dataSink, sink))
        {
            dataSink = null;
        }
    }

    private void HandleEventsUpdated(object? sender, AppEventsUpdatedEventArgs e)
    {
        RefreshEvents();
    }

    private void RefreshEvents()
    {
        if (Dispatcher?.IsDispatchRequired == true)
        {
            Dispatcher.Dispatch(RefreshEvents);
            return;
        }

        VisibleEvents.Clear();
        var sink = dataSink;
        if (sink == null)
        {
            return;
        }

        var channelLookup = sink.Channels?.ToDictionary(c => c.Id) ?? new Dictionary<byte, Channel>();

        foreach (var ansightEvent in sink.Events.OrderByDescending(e => e.CapturedAtUtc).Take(MaxEvents))
        {
            if (!channelLookup.TryGetValue(ansightEvent.Channel, out var channel))
            {
                continue;
            }

            VisibleEvents.Add(new AppEventDisplay()
            {
                Label = ansightEvent.Label,
                Symbol = AppEventLegend.GetSymbol(ansightEvent.Type),
                ChannelColor = channel.Color,
                Details = ansightEvent.Details,
                HasDetails = !string.IsNullOrWhiteSpace(ansightEvent.Details),
                Timestamp = ansightEvent.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss")
            });
        }
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);
        if (args.NewHandler == null)
        {
            Unsubscribe(dataSink);
        }
    }


    public void Detach()
    {
        Unsubscribe(dataSink);
    }
}
