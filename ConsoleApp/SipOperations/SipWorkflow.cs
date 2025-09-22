using ConsoleApp.States;

namespace ConsoleApp.SipOperations
{
    public class SipWorkflow
    {
        private readonly SipStateMachine _stateMachine;
        private readonly List<ISipOperation> _operations;

        public SipStateMachine StateMachine => _stateMachine;

        public SipWorkflow()
        {
            _stateMachine = new SipStateMachine();
            _operations = new List<ISipOperation>();

            // Подписываемся на изменения состояния
            _stateMachine.StateChanged += OnStateChanged;
        }

        public void AddOperation(ISipOperation operation)
        {
            _operations.Add(operation);
        }

        public async Task<bool> ExecuteWorkflowAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("🚀 Запуск SIP workflow...");

            _stateMachine.Reset();

            try
            {
                foreach (var operation in _operations)
                {
                    Console.WriteLine($"\n📋 Выполнение: {operation.OperationName}");

                    // Обновляем состояние в зависимости от операции
                    UpdateStateForOperation(operation);

                    bool success = await operation.ExecuteAsync(cancellationToken);

                    if (!success)
                    {
                        _stateMachine.TransitionTo(SipCallState.Failed);
                        Console.WriteLine($"❌ Операция {operation.OperationName} завершилась неудачно");
                        return false;
                    }

                    Console.WriteLine($"✅ Операция {operation.OperationName} выполнена успешно");
                }

                Console.WriteLine("\n🎉 Workflow завершен успешно!");
                return true;
            }
            catch (Exception ex)
            {
                _stateMachine.TransitionTo(SipCallState.Failed);
                Console.WriteLine($"❌ Ошибка в workflow: {ex.Message}");
                return false;
            }
        }

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

        private void OnStateChanged(SipCallState oldState, SipCallState newState)
        {
            Console.WriteLine($"📊 {_stateMachine.GetStateDescription(newState)}");
        }

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