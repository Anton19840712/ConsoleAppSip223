namespace ConsoleApp.CleanupHandlers
{
    public abstract class CleanupHandler
    {
        protected CleanupHandler? _next;

        public void SetNext(CleanupHandler handler) => _next = handler;

        public void Cleanup()
        {
            try
            {
                DoCleanup();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ⚠️ Ошибка в {GetType().Name}: {ex.Message}");
            }
            finally
            {
                _next?.Cleanup();
            }
        }

        protected abstract void DoCleanup();
    }
}