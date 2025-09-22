namespace ConsoleApp.States
{
    public class SipStateMachine
    {
        private SipCallState _currentState = SipCallState.Idle;

        public SipCallState CurrentState => _currentState;

        public event Action<SipCallState, SipCallState>? StateChanged;

        public bool CanTransitionTo(SipCallState newState)
        {
            return newState switch
            {
                SipCallState.Idle => true, // Всегда можно вернуться в Idle

                SipCallState.Registering => _currentState == SipCallState.Idle,

                SipCallState.Registered => _currentState == SipCallState.Registering,

                SipCallState.Calling => _currentState == SipCallState.Registered,

                SipCallState.Trying => _currentState == SipCallState.Calling,

                SipCallState.Ringing => _currentState == SipCallState.Trying,

                SipCallState.Connected => _currentState == SipCallState.Ringing,

                SipCallState.Disconnecting => _currentState == SipCallState.Connected ||
                                             _currentState == SipCallState.Ringing ||
                                             _currentState == SipCallState.Trying,

                SipCallState.Failed => _currentState != SipCallState.Idle &&
                                      _currentState != SipCallState.Finished,

                SipCallState.Finished => _currentState == SipCallState.Disconnecting,

                _ => false
            };
        }

        public bool TransitionTo(SipCallState newState)
        {
            if (!CanTransitionTo(newState))
            {
                Console.WriteLine($"⚠️ Недопустимый переход: {_currentState} → {newState}");
                return false;
            }

            var oldState = _currentState;
            _currentState = newState;

            Console.WriteLine($"🔄 Состояние: {oldState} → {newState}");
            StateChanged?.Invoke(oldState, newState);

            return true;
        }

        public void Reset()
        {
            TransitionTo(SipCallState.Idle);
        }

        public string GetStateDescription(SipCallState state)
        {
            return state switch
            {
                SipCallState.Idle => "Ожидание",
                SipCallState.Registering => "Регистрация на SIP сервере",
                SipCallState.Registered => "Зарегистрирован, готов к звонку",
                SipCallState.Calling => "Инициация звонка",
                SipCallState.Trying => "Сервер обрабатывает запрос",
                SipCallState.Ringing => "Звонок у абонента",
                SipCallState.Connected => "Разговор в процессе",
                SipCallState.Disconnecting => "Завершение звонка",
                SipCallState.Failed => "Ошибка",
                SipCallState.Finished => "Завершено",
                _ => "Неизвестное состояние"
            };
        }
    }
}