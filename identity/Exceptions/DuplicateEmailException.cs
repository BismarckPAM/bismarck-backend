namespace Identity.Service.Exceptions;

public class DuplicateEmailException(string email)
    : Exception($"A user with email '{email}' already exists.");
