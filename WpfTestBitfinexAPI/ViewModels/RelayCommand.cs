using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace WpfTestBitfinexAPI.ViewModels;

public class RelayCommand : ICommand
    {
        #region Поля и свойства
        /// <summary>
        /// Действие, которое будет выполняться при выполнении команды.
        /// </summary>
        private Action<object> execute;

    /// <summary>
    /// Функция, определяющая, может ли команда быть выполнена.
    /// </summary>
    private Func<object, bool> canExecute;
    #endregion

    #region Методы

    /// <summary>
    /// Определяет, может ли команда быть выполнена.
    /// </summary>
    /// <param name="parameter">Параметр команды.</param>
    /// <returns>Значение <see langword="true"/>, если команда может быть выполнена. Иначе - <see langword="false"/>.</returns>
    public bool CanExecute(object parameter)
    {
        return canExecute == null || canExecute(parameter);
    }
    /// <summary>
    /// Выполняет команду.
    /// </summary>
    /// <param name="parameter">Параметр команды.</param>                         
    public void Execute(object parameter)
    {
        execute(parameter);
    }
    #endregion

    #region События

    /// <summary>
    /// Событие, возникающее при изменении условий выполнения команды.
    /// Используется для автоматического обновления состояния команды.
    /// </summary>
    public event EventHandler CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
    #endregion

    #region Конструктор

    /// <summary>
    /// Создает новую команду с указанными действиями выполнения и проверки условий.
    /// </summary>
    /// <param name="execute">Действие для выполнения командой.</param>
    /// <param name="canExecute">Функция, определяющая, может ли команда выполняться. Если null, команда всегда может быть выполнена.</param>
    public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }
    #endregion


}
