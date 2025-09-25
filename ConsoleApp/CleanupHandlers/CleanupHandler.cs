namespace ConsoleApp.CleanupHandlers
{
    /// <summary>
    /// Абстрактный базовый класс для обработчиков очистки ресурсов по паттерну Chain of Responsibility
    /// </summary>
    public abstract class CleanupHandler
    {
        protected CleanupHandler? _next;

        /// <summary>
        /// Устанавливает следующий обработчик в цепочке очистки
        /// </summary>
        /// <param name="handler">Следующий обработчик</param>
        public void SetNext(CleanupHandler handler) => _next = handler;

        /// <summary>
        /// Запускает процесс очистки с обработкой исключений и передачей управления следующему обработчику
        /// </summary>
        public void Cleanup()
        {
            try
            {
                DoCleanup();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Предупреждение: Ошибка в {GetType().Name}: {ex.Message}");
            }
            finally
            {
                _next?.Cleanup();
            }
        }

        /// <summary>
        /// Абстрактный метод для выполнения специфической логики очистки в наследниках
        /// </summary>
        protected abstract void DoCleanup();
    }
}