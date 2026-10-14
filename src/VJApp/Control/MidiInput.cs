using System.Text.Json;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace VJApp.Control;

/// <summary>Uma ligação de MIDI learn: um controle físico → um endereço de controle do app.</summary>
sealed record MidiBinding(string Device, string Kind, int Channel, int Number, string Address)
{
    public string Key => $"{Device}|{Kind}|{Channel}|{Number}";
    public string Label => $"{(Kind == "cc" ? "CC" : "Nota")} {Number} (canal {Channel + 1})";
}

/// <summary>
/// Escuta todas as entradas MIDI. Com MIDI learn ativo, o próximo CC/nota recebido é ligado ao endereço
/// escolhido na UI; depois disso, mexer no controle manda o valor (0..1) para esse endereço.
/// </summary>
sealed class MidiInput : IDisposable
{
    static string BindingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VJApp", "midi.json");

    readonly List<InputDevice> _devices = [];
    readonly Action<string, float> _onControl;
    readonly object _lock = new();
    List<MidiBinding> _bindings;

    /// <summary>Endereço esperando o próximo controle MIDI (null = learn desligado).</summary>
    public string? Learning { get; private set; }
    public event Action? Changed;

    public IReadOnlyList<string> DeviceNames => _devices.Select(d => d.Name).ToList();
    public IReadOnlyList<MidiBinding> Bindings { get { lock (_lock) return [.. _bindings]; } }

    public MidiInput(Action<string, float> onControl)
    {
        _onControl = onControl;
        _bindings = Load();
        foreach (var device in InputDevice.GetAll())
        {
            try
            {
                device.EventReceived += (_, e) => OnEvent(device.Name, e.Event);
                device.StartEventsListening();
                _devices.Add(device);
            }
            catch
            {
                device.Dispose(); // dispositivo ocupado por outro programa
            }
        }
    }

    public void StartLearn(string address) { Learning = address; Changed?.Invoke(); }
    public void CancelLearn() { Learning = null; Changed?.Invoke(); }

    public void ClearBindings()
    {
        lock (_lock) _bindings.Clear();
        Save();
        Changed?.Invoke();
    }

    public void RemoveBinding(string address)
    {
        lock (_lock) _bindings.RemoveAll(b => b.Address == address);
        Save();
        Changed?.Invoke();
    }

    void OnEvent(string device, MidiEvent ev)
    {
        (string kind, int channel, int number, float value)? msg = ev switch
        {
            ControlChangeEvent cc => ("cc", cc.Channel, cc.ControlNumber, cc.ControlValue / 127f),
            NoteOnEvent on => ("note", on.Channel, on.NoteNumber, on.Velocity > 0 ? 1f : 0f),
            NoteOffEvent off => ("note", off.Channel, off.NoteNumber, 0f),
            _ => null,
        };
        if (msg is not { } m) return;

        if (Learning is { } address && (m.kind == "cc" || m.value > 0))
        {
            var binding = new MidiBinding(device, m.kind, m.channel, m.number, address);
            lock (_lock)
            {
                _bindings.RemoveAll(b => b.Key == binding.Key || b.Address == address);
                _bindings.Add(binding);
            }
            Learning = null;
            Save();
            Changed?.Invoke();
            return;
        }

        string key = $"{device}|{m.kind}|{m.channel}|{m.number}";
        List<MidiBinding> hits;
        lock (_lock) hits = _bindings.Where(b => b.Key == key).ToList();
        foreach (var b in hits) _onControl(b.Address, m.value);
    }

    static List<MidiBinding> Load()
    {
        try
        {
            if (File.Exists(BindingsPath))
                return JsonSerializer.Deserialize<List<MidiBinding>>(File.ReadAllText(BindingsPath)) ?? [];
        }
        catch { }
        return [];
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BindingsPath)!);
        lock (_lock) File.WriteAllText(BindingsPath, JsonSerializer.Serialize(_bindings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        foreach (var d in _devices) { try { d.StopEventsListening(); } catch { } d.Dispose(); }
        _devices.Clear();
    }
}
