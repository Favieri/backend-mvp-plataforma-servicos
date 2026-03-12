namespace Application.Abstractions;

public interface IAntiLeakDetectionService
{
    /// <summary>
    /// Analisa o texto de uma mensagem em busca de padrões de contato externo
    /// (telefone, e-mail, URLs). Retorna true se algum padrão for detectado.
    /// </summary>
    bool HasLeakPattern(string text);

    /// <summary>
    /// Retorna a mensagem de aviso educativo a ser inserida como mensagem do tipo "system"
    /// quando um padrão de fuga é detectado.
    /// </summary>
    string GetWarningText();
}
