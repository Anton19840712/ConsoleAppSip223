using ConsoleApp.States;
using Microsoft.Extensions.Logging;

namespace ConsoleApp.SipOperations
{
    /// <summary>
    /// Класс для управления рабочим процессом SIP операций (регистрация, звонок)
    /// </summary>
    public class SipWorkflow
    {
        private readonly SipStateMachine _stateMachine;
        private readonly List<ISipOperation> _operations;
        private readonly ILogger<SipWorkflow> _logger;

        public SipStateMachine StateMachine => _stateMachine;

        /// <summary>
        /// Инициализирует новый экземпляр SIP workflow с машиной состояний
        /// </summary>
        public SipWorkflow(ILogger<SipWorkflow> logger, ILogger<SipStateMachine> stateMachineLogger)
        {
            _logger = logger;
            // Инициализируем машину состояний для отслеживания этапов звонка
            _stateMachine = new SipStateMachine(stateMachineLogger);
            // Создаём список для хранения операций, которые будут выполнены последовательно
            _operations = new List<ISipOperation>();

            // Подписываемся на события смены состояния для вывода логов
            _stateMachine.StateChanged += OnStateChanged;
        }

        /// <summary>
        /// Добавляет операцию в рабочий процесс
        /// </summary>
        /// <param name="operation">SIP операция для добавления</param>
        public void AddOperation(ISipOperation operation)
        {
            // Добавляем операцию в конец списка (они будут выполняться в том порядке, в котором добавлялись)
            _operations.Add(operation);
        }

        /// <summary>
        /// Асинхронно выполняет все операции в рабочем процессе
        /// </summary>
        /// <param name="cancellationToken">Токен для отмены операции</param>
        /// <returns>true, если все операции выполнены успешно; иначе false</returns>
        public async Task<bool> ExecuteWorkflowAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Запуск SIP workflow...");

            // Сбрасываем машину состояний в начальное состояние перед началом нового workflow
            _stateMachine.Reset();

            try
            {
                // Проходим по всем операциям в порядке добавления и выполняем их последовательно
                foreach (var operation in _operations)
                {
                    _logger.LogInformation("Выполнение: {OperationName}", operation.OperationName);

                    // Обновляем состояние машины состояний в зависимости от типа операции
                    UpdateStateForOperation(operation);

                    // Выполняем саму операцию асинхронно и получаем результат
                    bool success = await operation.ExecuteAsync(cancellationToken);

                    // Проверяем результат выполнения операции
                    if (!success)
                    {
                        // При ошибке переводим машину состояний в состояние "ошибка" и прекращаем workflow
                        _stateMachine.TransitionTo(SipCallState.Failed);
                        _logger.LogError("Операция {OperationName} завершилась неудачно", operation.OperationName);
                        return false;
                    }

                    // После успешного выполнения операции обновляем состояние
                    UpdateStateAfterSuccessfulOperation(operation);

                    _logger.LogInformation("Операция {OperationName} выполнена успешно", operation.OperationName);
                }

                _logger.LogInformation("Workflow завершен успешно!");
                return true;
            }
            catch (Exception ex)
            {
                _stateMachine.TransitionTo(SipCallState.Failed);
                _logger.LogError(ex, "Ошибка в workflow: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Обновляет состояние машины состояний в зависимости от типа операции
        /// </summary>
        /// <param name="operation">Операция, для которой обновляется состояние</param>
        private void UpdateStateForOperation(ISipOperation operation)
        {
            switch (operation.OperationName)
            {
                case "SIP Registration":
                    _stateMachine.TransitionTo(SipCallState.Registering);
                    break;
                case "SIP Call":
                    _stateMachine.TransitionTo(SipCallState.Calling);
                    break;
            }
        }

        /// <summary>
        /// Обновляет состояние машины состояний после успешного выполнения операции
        /// </summary>
        /// <param name="operation">Успешно выполненная операция</param>
        private void UpdateStateAfterSuccessfulOperation(ISipOperation operation)
        {
            switch (operation.OperationName)
            {
                case "SIP Registration":
                    _stateMachine.TransitionTo(SipCallState.Registered);
                    _logger.LogInformation("SIP регистрация успешно завершена!");
                    break;
                case "SIP Call":
                    // Для звонков состояние будет обновляться через события SIP
                    break;
            }
        }

        /// <summary>
        /// Обрабатывает событие смены состояния в машине состояний
        /// </summary>
        /// <param name="oldState">Предыдущее состояние</param>
        /// <param name="newState">Новое состояние</param>
        private void OnStateChanged(SipCallState oldState, SipCallState newState)
        {
            _logger.LogInformation("{StateDescription}", _stateMachine.GetStateDescription(newState));
        }

        /// <summary>
        /// Обрабатывает SIP события и обновляет соответствующее состояние
        /// </summary>
        /// <param name="eventType">Тип SIP события</param>
        public void HandleSipEvent(string eventType)
        {
            switch (eventType)
            {
                case "Registered":
                    _stateMachine.TransitionTo(SipCallState.Registered);
                    break;
                case "Calling":
                    _stateMachine.TransitionTo(SipCallState.Calling);
                    break;
                case "Trying":
                    _stateMachine.TransitionTo(SipCallState.Trying);
                    break;
                case "Ringing":
                    _stateMachine.TransitionTo(SipCallState.Ringing);
                    break;
                case "Answered":
                    _stateMachine.TransitionTo(SipCallState.Connected);
                    break;
                case "Failed":
                    _stateMachine.TransitionTo(SipCallState.Failed);
                    break;
                case "Hangup":
                    _stateMachine.TransitionTo(SipCallState.Disconnecting);
                    break;
            }
        }
    }
}