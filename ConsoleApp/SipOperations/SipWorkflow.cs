using ConsoleApp.States;

namespace ConsoleApp.SipOperations
{
    /// <summary>
    /// Класс для управления рабочим процессом SIP операций (регистрация, звонок)
    /// </summary>
    public class SipWorkflow
    {
        private readonly SipStateMachine _stateMachine;
        private readonly List<ISipOperation> _operations;

        public SipStateMachine StateMachine => _stateMachine;

        /// <summary>
        /// Инициализирует новый экземпляр SIP workflow с машиной состояний
        /// </summary>
        public SipWorkflow()
        {
            // Инициализируем машину состояний для отслеживания этапов звонка
            _stateMachine = new SipStateMachine();
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
            Console.WriteLine("Запуск SIP workflow...");

            // Сбрасываем машину состояний в начальное состояние перед началом нового workflow
            _stateMachine.Reset();

            try
            {
                // Проходим по всем операциям в порядке добавления и выполняем их последовательно
                foreach (var operation in _operations)
                {
                    Console.WriteLine($"\nВыполнение: {operation.OperationName}");

                    // Обновляем состояние машины состояний в зависимости от типа операции
                    UpdateStateForOperation(operation);

                    // Выполняем саму операцию асинхронно и получаем результат
                    bool success = await operation.ExecuteAsync(cancellationToken);

                    // Проверяем результат выполнения операции
                    if (!success)
                    {
                        // При ошибке переводим машину состояний в состояние "ошибка" и прекращаем workflow
                        _stateMachine.TransitionTo(SipCallState.Failed);
                        Console.WriteLine($"Операция {operation.OperationName} завершилась неудачно");
                        return false;
                    }

                    Console.WriteLine($"Операция {operation.OperationName} выполнена успешно");
                }

                Console.WriteLine("\nWorkflow завершен успешно!");
                return true;
            }
            catch (Exception ex)
            {
                _stateMachine.TransitionTo(SipCallState.Failed);
                Console.WriteLine($"Ошибка в workflow: {ex.Message}");
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
        /// Обрабатывает событие смены состояния в машине состояний
        /// </summary>
        /// <param name="oldState">Предыдущее состояние</param>
        /// <param name="newState">Новое состояние</param>
        private void OnStateChanged(SipCallState oldState, SipCallState newState)
        {
            Console.WriteLine($"{_stateMachine.GetStateDescription(newState)}");
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