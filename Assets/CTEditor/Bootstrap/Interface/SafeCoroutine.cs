using System;
using System.Collections;
using System.Collections.Generic;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// CORRUTINAS A PRUEBA DE ERRORES. Unity, si una corrutina anidada lanza una excepción, deja a la de
    /// arriba parada PARA SIEMPRE (sin ejecutar sus finally): el menú se quedaría abierto y el jugador
    /// atascado. Esto ejecuta la cadena de corrutinas anidadas a mano, captura el error, cierra todo con
    /// orden (Dispose ejecuta los finally) y avisa con 'onError'.
    ///   StartCoroutine(SafeCoroutine.Run(MiPantalla(), e => ...));
    /// </summary>
    public static class SafeCoroutine
    {
        public static IEnumerator Run(IEnumerator root, Action<Exception> onError)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            try
            {
                while (stack.Count > 0)
                {
                    var top = stack.Peek();
                    bool moved;
                    Exception error = null;
                    try { moved = top.MoveNext(); }
                    catch (Exception e) { moved = false; error = e; }
                    if (error != null) { onError?.Invoke(error); yield break; }
                    if (!moved) { stack.Pop(); continue; }
                    if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    yield return top.Current; // WaitForSeconds, null...: los resuelve Unity
                }
            }
            finally
            {
                // Pase lo que pase (error, fin o corrutina parada y liberada), se cierran las que queden.
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            }
        }
    }
}
