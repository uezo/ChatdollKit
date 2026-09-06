// Adapted to C# from AIAvatarKit (uezo, Apache-2.0), revision d775070.
// See SpeechPipeline/README.ja.md for compatibility and lifecycle decisions.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.SpeechPipeline.Async;

namespace ChatdollKit.SpeechPipeline.VAD.TurnEndGates
{
    /// <summary>
    /// Evaluates gates in order and latches WAIT decisions until additional audio
    /// silence reaches their timeout. Feed each session sequentially; reset may
    /// interrupt evaluation. Background gates receive the preceding context only.
    /// </summary>
    public sealed class TurnEndGateManager : IDisposable
    {
        private sealed class HoldState
        {
            public bool Active;
            public bool Evaluating;
            public double? Timeout;
            public readonly List<string> Reasons = new List<string>();
            public readonly Dictionary<string, TurnEndDecision> WaitDecisions = new Dictionary<string, TurnEndDecision>();
            public readonly Dictionary<string, UniTask<TurnEndDecision>> Pending = new Dictionary<string, UniTask<TurnEndDecision>>();
            public readonly CancellationTokenSource Cancellation;
            public readonly CancellationToken Token;

            public HoldState(CancellationToken token)
            {
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                Token = Cancellation.Token;
            }
        }

        private readonly object sync = new object();
        private readonly ITurnEndGate[] gates;
        private readonly Action<Exception> onError;
        private readonly SpeechCallbackGuard callbacks;
        private readonly Dictionary<string, HoldState> states = new Dictionary<string, HoldState>();
        private readonly HashSet<UniTask> observations = new HashSet<UniTask>();
        private bool disposed;
        public bool HasGates => gates.Length != 0;

        public TurnEndGateManager(IEnumerable<ITurnEndGate> gates, Action<Exception> onError = null,
            SpeechCallbackGuard callbacks = null)
        {
            this.gates = gates?.ToArray() ?? new ITurnEndGate[0];
            if (this.gates.Any(gate => gate == null)) throw new ArgumentException("Gates cannot contain null.", nameof(gates));
            if (this.gates.Select(gate => gate.Name).Distinct().Count() != this.gates.Length)
                throw new ArgumentException("Gate names must be unique within a manager.", nameof(gates));
            this.onError = onError;
            this.callbacks = callbacks ?? new SpeechCallbackGuard();
        }

        public TurnEndGateState GetSessionState(string sessionId)
        {
            lock (sync)
            {
                return states.TryGetValue(sessionId, out var state)
                    ? new TurnEndGateState(state.Active, state.Timeout, state.Reasons)
                    : new TurnEndGateState(false, null, new string[0]);
            }
        }

        public async UniTask<bool> ShouldEndTurnAsync(TurnEndRequest request, double silenceDurationThreshold, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.SessionId == null) throw new ArgumentException("SessionId is required.", nameof(request));
            token.ThrowIfCancellationRequested();
            HoldState state;
            bool completedHold = false;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(TurnEndGateManager));
                if (!HasGates) return true;
                if (!states.TryGetValue(request.SessionId, out state))
                {
                    state = new HoldState(token);
                    states.Add(request.SessionId, state);
                }
                if (state.Evaluating) throw new InvalidOperationException("Gate evaluations for the same session must be sequential.");
                if (state.Active)
                {
                    HarvestPending(state);
                    double holdDuration = Math.Max(0, request.SilenceDuration - silenceDurationThreshold);
                    completedHold = (state.WaitDecisions.Count == 0 && state.Pending.Count == 0)
                        || (state.Timeout.HasValue && holdDuration >= state.Timeout.Value);
                    if (!completedHold) return false;
                    states.Remove(request.SessionId);
                }
                else state.Evaluating = true;
            }
            if (completedHold)
            {
                CancelState(state);
                return true;
            }

            try
            {
                var context = new TurnEndGateContext();
                foreach (var gate in gates)
                {
                    state.Token.ThrowIfCancellationRequested();
                    bool background = gate.RunInBackground && callbacks.Invoke(() => gate.ShouldRunInBackground(context));
                    var gateRequest = request.WithContext(background ? context.Snapshot() : context);
                    var task = SpeechAsync.Share(InvokeGateAsync(gate, gateRequest, state.Token));
                    Observe(task.AsUniTask(), background);
                    TurnEndDecision decision;
                    if (background)
                    {
                        decision = new TurnEndDecision { ShouldEnd = false, Pending = true, Reason = gate.Name + "_pending", Timeout = gate.Timeout };
                        lock (sync) state.Pending[gate.Name] = task;
                    }
                    else decision = await task;
                    state.Token.ThrowIfCancellationRequested();

                    context.AddDecision(gate.Name, decision);
                    lock (sync)
                    {
                        if (!IsCurrent(request.SessionId, state)) return false;
                        if (!decision.ShouldEnd) state.WaitDecisions[gate.Name] = decision;
                    }
                }

                lock (sync)
                {
                    if (!IsCurrent(request.SessionId, state)) return false;
                    state.Evaluating = false;
                    if (state.WaitDecisions.Count != 0)
                    {
                        state.Active = true;
                        Recalculate(state);
                        return false;
                    }
                    states.Remove(request.SessionId);
                }
                CancelState(state);
                return true;
            }
            catch (OperationCanceledException)
            {
                RemoveState(request.SessionId, state);
                token.ThrowIfCancellationRequested();
                return false;
            }
            catch (Exception error)
            {
                // An evaluation failure falls back to the normal VAD turn end.
                bool current = RemoveState(request.SessionId, state);
                Report(error);
                return current;
            }
        }

        private async UniTask<TurnEndDecision> InvokeGateAsync(ITurnEndGate gate, TurnEndRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var decision = await callbacks.InvokeAsync(() => gate.ShouldEndTurnAsync(request, token));
            if (decision == null) throw new InvalidOperationException("A turn-end gate returned no decision.");
            return decision;
        }

        private static void HarvestPending(HoldState state)
        {
            foreach (string name in state.Pending.Where(pair => pair.Value.Status.IsCompleted()).Select(pair => pair.Key).ToArray())
            {
                var task = state.Pending[name];
                state.Pending.Remove(name);
                if (task.Status == UniTaskStatus.Succeeded && !task.GetAwaiter().GetResult().ShouldEnd)
                    state.WaitDecisions[name] = task.GetAwaiter().GetResult();
                else state.WaitDecisions.Remove(name); // Failed/cancelled background gates do not hold the turn.
            }
            Recalculate(state);
        }

        private static void Recalculate(HoldState state)
        {
            state.Reasons.Clear();
            double? timeout = 0;
            foreach (var decision in state.WaitDecisions.Values)
            {
                state.Reasons.Add(decision.Reason ?? "wait");
                if (!decision.Timeout.HasValue) timeout = null;
                else if (timeout.HasValue) timeout = Math.Max(timeout.Value, decision.Timeout.Value);
            }
            state.Timeout = state.WaitDecisions.Count == 0 ? null : timeout;
        }

        public void ResetSession(string sessionId) => RemoveState(sessionId, null);

        private bool RemoveState(string sessionId, HoldState expected)
        {
            HoldState removed;
            lock (sync)
            {
                if (!states.TryGetValue(sessionId, out removed) || (expected != null && !ReferenceEquals(removed, expected))) return false;
                states.Remove(sessionId);
            }
            CancelState(removed);
            return true;
        }

        private bool IsCurrent(string sessionId, HoldState state)
            => states.TryGetValue(sessionId, out var current) && ReferenceEquals(current, state);

        private void CancelState(HoldState state)
        {
            try { state.Cancellation.Cancel(); }
            catch (Exception error) { Report(error); }
            finally { state.Cancellation.Dispose(); }
        }

        private void Observe(UniTask task, bool reportErrors)
        {
            var completion = new SpeechCompletionSource<bool>();
            lock (sync) observations.Add(completion.Task.AsUniTask());
            _ = ObserveAsync(task, completion, reportErrors);
        }

        private async UniTask ObserveAsync(UniTask task, SpeechCompletionSource<bool> completion, bool reportErrors)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (reportErrors) Report(error); }
            finally
            {
                lock (sync) observations.Remove(completion.Task.AsUniTask());
                completion.TrySetResult(true);
            }
        }

        /// <summary>Waits for all started gate work, including work cancelled by reset.</summary>
        public async UniTask DrainAsync()
        {
            while (true)
            {
                UniTask[] pending;
                lock (sync) pending = observations.ToArray();
                if (pending.Length == 0) return;
                await SpeechAsync.WhenAll(pending);
            }
        }

        private void Report(Exception error)
        {
            try { onError?.Invoke(error); }
            catch { /* Diagnostics must not change gate policy or teardown. */ }
        }

        public void Dispose()
        {
            HoldState[] removed;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                removed = states.Values.ToArray();
                states.Clear();
            }
            foreach (var state in removed) CancelState(state);
        }
    }
}
