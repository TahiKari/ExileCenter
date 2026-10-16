namespace GameHelper.RemoteObjects.Components
{
    using GameOffsets.Natives;
    using GameOffsets.Objects.Components;
    using ImGuiNET;
    using System;
    using System.Collections.Generic;

    public class StateMachine : ComponentBase
    {
        public StateMachine(IntPtr address) : base(address) { }

        private const int StateStructSize = 0xC0;

        public IReadOnlyList<StateMachineState> States { get; private set; } = [];

        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"State Count: {States.Count}");
            for (int i = 0; i < States.Count; i++)
            {
                var state = States[i];
                ImGui.Text($"State[{i}]: Name='{state.Name}', Value={state.Value}");
            }
        }

        private const int MaxStateMachineStates = 256;

        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<StateMachineComponentOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;

            var stateValues = reader.ReadStdVector<long>(data.StatesValues);
            var statesCount = stateValues.Length;
            if (statesCount > MaxStateMachineStates)
            {
                Console.WriteLine($"[StateMachine] Suspicious state count {statesCount} at 0x{this.Address.ToInt64():X}; capping to {MaxStateMachineStates} (audit F-120).");
                statesCount = MaxStateMachineStates;
            }

            var statesPtr = reader.ReadMemory<IntPtr>(data.StatesPtr + 0x10);
            if (statesPtr == IntPtr.Zero)
            {
                this.States = [];
                return;
            }

            var statesList = new List<StateMachineState>(statesCount);
            for (var i = 0; i < statesCount; i++)
            {
                var stateNameAddr = statesPtr + i * StateStructSize;
                var nativeContainer = reader.ReadMemory<StdString>(stateNameAddr);
                var stateName = reader.ReadStdString(nativeContainer);
                statesList.Add(new StateMachineState(stateName, stateValues[i]));
            }

            this.States = statesList;
        }

        // Offsets for resolving the authoritative socket/hole count from the RuneStation object
        // that listens on this StateMachine (the "sockets" state caps at the model's 6 physical
        // socket props and under-reports recipes that use more, e.g. 7-hole Transcendent Alloy).
        private const int ListenerVectorOffset = 0x20;  // SM + 0x20 : std::vector<listener*> {begin,end,cap}
        private const int StationFromListener = 0x98;    // station = *(node) - 0x98
        private const int StationDeviceBackPtr = 0x10;   // station + 0x10 : back-ptr to device entity
        private const int StationSocketCount = 0x38;     // station + 0x38 : int socket/hole count

        /// <summary>
        ///     Attempts to read the authoritative socket/hole count from the RuneStation object
        ///     that listens on this StateMachine. Walks the listener vector at SM + 0x20, resolves
        ///     each listener back to its station (station = *(node) - 0x98), and verifies the
        ///     station's device back-pointer (station + 0x10) matches this component's owner entity
        ///     before reading the count at station + 0x38. Works out of the network bubble since the
        ///     station persists. Callers should fall back to the "sockets" state if this returns false.
        /// </summary>
        /// <param name="count">The resolved socket/hole count, when found.</param>
        /// <returns>True if a matching RuneStation was resolved; otherwise false.</returns>
        public bool TryGetRuneStationSocketCount(out int count)
        {
            count = 0;
            if (this.Address == IntPtr.Zero || this.OwnerEntityAddress == IntPtr.Zero)
            {
                return false;
            }

            var reader = Core.Process.Handle;
            var listeners = reader.ReadMemory<StdVector>(this.Address + ListenerVectorOffset);
            var nodes = reader.ReadStdVector<long>(listeners);
            foreach (var nodeValue in nodes)
            {
                if (nodeValue == 0)
                {
                    continue;
                }

                var sub = reader.ReadMemory<IntPtr>(new IntPtr(nodeValue));
                if (sub == IntPtr.Zero)
                {
                    continue;
                }

                var station = sub - StationFromListener;
                var deviceBackPtr = reader.ReadMemory<IntPtr>(station + StationDeviceBackPtr);
                if (deviceBackPtr != this.OwnerEntityAddress)
                {
                    continue;
                }

                count = reader.ReadMemory<int>(station + StationSocketCount);
                return true;
            }

            return false;
        }
    }

    public class StateMachineState(string name, long value)
    {
        public string Name { get; } = name;
        public long Value { get; } = value;

        public override string ToString() => $"{Name}: {Value}";
    }
}

