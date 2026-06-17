using System;

namespace CTEditor.SharedKernel.ValueObjects
{
    /// <summary>
    /// Éxito o fallo SIN excepciones. El dominio devuelve Result en vez de lanzar o loguear (J.2):
    /// un comando rechazado (A.7) es un Failure, no una excepción. La capa de afuera decide qué hacer.
    /// </summary>
    public readonly struct Result
    {
        public bool IsSuccess { get; }
        public string Error { get; }
        public bool IsFailure => !IsSuccess;

        private Result(bool isSuccess, string error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        public static Result Success() => new Result(true, null);
        public static Result Failure(string error) => new Result(false, error);

        public static Result<T> Success<T>(T value) => Result<T>.Ok(value);
        public static Result<T> Failure<T>(string error) => Result<T>.Fail(error);
    }

    /// <summary>Result que, en caso de éxito, transporta un valor.</summary>
    public readonly struct Result<T>
    {
        public bool IsSuccess { get; }
        public T Value { get; }
        public string Error { get; }
        public bool IsFailure => !IsSuccess;

        private Result(bool isSuccess, T value, string error)
        {
            IsSuccess = isSuccess;
            Value = value;
            Error = error;
        }

        public static Result<T> Ok(T value) => new Result<T>(true, value, null);
        public static Result<T> Fail(string error) => new Result<T>(false, default, error);
    }
}
