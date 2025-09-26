using Microsoft.Extensions.Logging;

namespace ConsoleApp.States
{
    /// <summary>
    /// Машина состояний для отслеживания состояния SIP звонка
    /// </summary>
    public class SipStateMachine
    {
        private SipCallState _currentState = SipCallState.Idle;
        private readonly ILogger<SipStateMachine> _logger;

        public SipStateMachine(ILogger<SipStateMachine> logger)
        {
            _logger = logger;
        }

        public SipCallState CurrentState => _currentState;

        public event Action<SipCallState, SipCallState>? StateChanged;

        /// <summary>
        /// Проверяет, возможен ли переход в указанное новое состояние
        /// </summary>
        /// <param name="newState">Новое состояние для перехода</param>
        /// <returns>true, если переход возможен; иначе false</returns>
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

        /// <summary>
        /// Выполняет переход в новое состояние, если он допустим
        /// </summary>
        /// <param name="newState">Новое состояние</param>
        /// <returns>true, если переход выполнен успешно; иначе false</returns>
        public bool TransitionTo(SipCallState newState)
        {
            if (!CanTransitionTo(newState))
            {
                _logger.LogWarning("Недопустимый переход: {CurrentState} → {NewState}", _currentState, newState);
                return false;
            }

            var oldState = _currentState;
            _currentState = newState;

            _logger.LogInformation("Состояние: {OldState} → {NewState}", oldState, newState);
            StateChanged?.Invoke(oldState, newState);

            return true;
        }

        /// <summary>
        /// Сбрасывает машину состояний в начальное состояние (Idle)
        /// </summary>
        public void Reset()
        {
            TransitionTo(SipCallState.Idle);
        }

        /// <summary>
        /// Получает читаемое описание состояния на русском языке
        /// </summary>
        /// <param name="state">Состояние для описания</param>
        /// <returns>Описание состояния на русском языке</returns>
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