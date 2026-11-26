// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;

namespace SalvaOPM
{
    public enum Mode { Eco = 0, Balanced = 1, Performance = 2 }

    /// <summary>What the caller must do after an event. No I/O happens in this file.</summary>
    public sealed class Outcome
    {
        public Mode Effective;     // mode that must be active now
        public bool Auto;          // Effective is an automatic Eco (power lost)
        public bool Apply;         // the effective mode must be (re)applied to the firmware
        public bool Rejected;      // a manual request that was refused (Unleashed without power)
        public bool SaveChanged;   // SavedMode changed and must be written to disk
        public Mode SavedMode;     // value to persist
    }

    /// <summary>
    /// All mode decisions (OGH's power rules, docs/firmware.md):
    ///  - persisted value is only Eco or Balanced; Unleashed is saved as Balanced (so a boot after Unleashed gives Balanced);
    ///  - losing power (unplug or a charger below requirement) switches to an automatic Eco, unless Eco was already chosen by hand;
    ///  - power back while the Eco was automatic -> always Balanced;
    ///  - Unleashed cannot be chosen without power; a manual choice cancels the automatic state.
    /// </summary>
    public sealed class ModeController
    {
        private Mode _user = Mode.Balanced;
        private bool _auto;
        private bool _powerOk = true;
        private Mode _saved = Mode.Balanced;

        public Mode Effective { get { return _auto ? Mode.Eco : _user; } }
        public bool AutoEco { get { return _auto; } }
        public Mode SavedMode { get { return _saved; } }
        public bool PerformanceAllowed { get { return _powerOk; } }

        private static Mode Normalize(Mode m) { return m == Mode.Eco ? Mode.Eco : Mode.Balanced; }

        private Outcome Result(bool apply, bool saveChanged)
        {
            Outcome o = new Outcome();
            o.Effective = Effective;
            o.Auto = _auto;
            o.Apply = apply;
            o.SaveChanged = saveChanged;
            o.SavedMode = _saved;
            return o;
        }

        /// <summary>Start-up (logon). 'saved' is the persisted value, 'powerOk' the current power state.</summary>
        public Outcome Boot(Mode saved, bool powerOk)
        {
            _saved = Normalize(saved);
            _powerOk = powerOk;
            if (!powerOk && _saved != Mode.Eco) { _user = Mode.Balanced; _auto = true; }
            else { _user = _saved; _auto = false; }
            return Result(true, false);
        }

        /// <summary>The user clicked a button.</summary>
        public Outcome Select(Mode m)
        {
            if (m == Mode.Performance && !_powerOk)
            {
                Outcome r = Result(false, false);
                r.Rejected = true;
                return r;
            }
            Mode before = Effective;
            _user = m;
            _auto = false;
            Mode s = Normalize(m);
            bool changed = s != _saved;
            _saved = s;
            return Result(m != before, changed);
        }

        /// <summary>AC/DC or charger-quality change. powerOk = on AC and the charger is not below requirement.</summary>
        public Outcome PowerChanged(bool powerOk)
        {
            if (powerOk == _powerOk) return Result(false, false);
            _powerOk = powerOk;
            Mode before = Effective;
            if (!powerOk) { if (_user != Mode.Eco) _auto = true; }
            else if (_auto) { _auto = false; _user = Mode.Balanced; }
            return Result(Effective != before, false);
        }

        /// <summary>Resume from sleep/hibernate: apply any missed transition, then always re-apply (firmware state has expired).</summary>
        public Outcome Resume(bool powerOk)
        {
            Outcome o = PowerChanged(powerOk);
            o.Apply = true;
            return o;
        }
    }
}
