﻿using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Reflection;
using Task.Integration.Data.DbCommon;
using Task.Integration.Data.DbCommon.DbModels;
using Task.Integration.Data.Models;
using Task.Integration.Data.Models.Models;

namespace Task.Connector
{
    public class ConnectorDb : IConnector
    {
        private const string RequestRightGroupName = "Request";
        private const string ItRoleRightGroupName = "Role";
        private static string Delimiter = ":";

        private static readonly Dictionary<string, PropertyInfo> _userProperties = typeof(User)
            .GetProperties()
            .Where(p => p.Name != nameof(User.Login))
            .ToDictionary(p => p.Name.ToLower(), p => p);

        private DbContextFactory _dbContextFactory;
        private DataContext _dataContext;

        public ILogger Logger { get; set; }

        public void StartUp(string connectionString)
        {
            _dbContextFactory = new DbContextFactory(connectionString);
            _dataContext = _dbContextFactory.GetContext("POSTGRE");
        }

        public void CreateUser(UserToCreate user)
        {
            Logger.Debug($"Creating user...");

            if (IsUserExists(user.Login))
            {
                Logger.Error($"The user with login '{user.Login}' already exists.");
                return;
            }
            if (user.Login.Length > 22)
            {
                Logger.Error("The length of login can not exceed 22.");
                return;
            }
            if (user.HashPassword.Length > 20)
            {
                Logger.Error("The length of password can not exceed 20.");
                return;
            }

            var createdUser = new User
            {
                Login = user.Login,
                LastName = string.Empty,
                FirstName = string.Empty,
                MiddleName = string.Empty,
                TelephoneNumber = string.Empty,
                IsLead = false
            };
            SetUserProperties(createdUser, user.Properties);
            _dataContext.Users.Add(createdUser);

            var password = new Sequrity
            {
                UserId = createdUser.Login,
                Password = user.HashPassword
            };
            _dataContext.Passwords.Add(password);

            try
            {
                Logger.Debug("Saving changes...");
                _dataContext.SaveChanges();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving user: {ex.Message}");
                return;
            }
            Logger.Debug("User added successfully.");
        }

        public IEnumerable<Property> GetAllProperties()
        {
            Logger.Debug("Getting all properties...");

            var properties = _userProperties.Values
                .Select(p => new Property(p.Name, string.Empty))
                .ToList();
            properties.Add(new Property("Password", string.Empty));

            Logger.Debug($"Properties retrieved: {properties.Count}.");
            return properties;
        }

        public IEnumerable<UserProperty> GetUserProperties(string userLogin)
        {
            Logger.Debug($"Getting properties of '{userLogin}'...");

            var user = GetUser(userLogin);
            if (user == null)
            {
                Logger.Warn("Returning an empty enumerable!");
                return Enumerable.Empty<UserProperty>();
            }

            var properties = _userProperties.Values
                .Select(p => new UserProperty(p.Name, p.GetValue(user)!.ToString()!));

            Logger.Debug($"Properties retrieved: {properties.Count()}");
            return properties;
        }

        public bool IsUserExists(string userLogin)
        {
            return _dataContext.Users.AsNoTracking().Any(u => u.Login == userLogin);
        }

        public void UpdateUserProperties(IEnumerable<UserProperty> properties, string userLogin)
        {
            Logger.Debug($"Updating properties of '{userLogin}'...");

            var user = GetUser(userLogin);
            if (user == null)
            {
                return;
            }

            SetUserProperties(user, properties);

            try
            {
                Logger.Debug("Saving changes...");
                _dataContext.SaveChanges();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving user: {ex.Message}");
                return;
            }

            Logger.Debug($"Properties updated successfully for '{userLogin}'.");
        }

        public IEnumerable<Permission> GetAllPermissions()
        {
            Logger.Debug("Getting all permissions...");

            var requestRights = _dataContext.RequestRights
                .AsNoTracking()
                .Select(rr => new Permission(rr.Id.ToString()!, rr.Name, string.Empty))
                .ToList();
            Logger.Debug($"RequestRights retrieved: {requestRights.Count}");

            var itRoles = _dataContext.ITRoles
                .AsNoTracking()
                .Select(role => new Permission(role.Id.ToString()!, role.Name, string.Empty))
                .ToList();
            Logger.Debug($"RequestRights retrieved: {requestRights.Count}");

            var permissions = requestRights.Concat(itRoles);
            Logger.Debug($"Permissions retrieved: {permissions.Count()}");

            return permissions;
        }

        public void AddUserPermissions(string userLogin, IEnumerable<string> rightIds)
        {
            Logger.Debug($"Adding permissions for '{userLogin}'...");

            if (!IsUserExists(userLogin))
            {
                Logger.Error($"The user with login '{userLogin}' does not exist.");
                return;
            }

            foreach (var rightId in rightIds)
            {
                var right = rightId.Split(Delimiter);
                if (right[0] == ItRoleRightGroupName)
                {
                    var userITRole = new UserITRole
                    { 
                        UserId = userLogin,
                        RoleId = int.Parse(right[1])
                    };
                    _dataContext.UserITRoles.Add(userITRole);
                }
                else if (right[0] == RequestRightGroupName)
                {
                    var userRequestRight = new UserRequestRight
                    {
                        UserId = userLogin,
                        RightId = int.Parse(right[1])
                    };
                    _dataContext.UserRequestRights.Add(userRequestRight);
                }
                else
                {
                    Logger.Error($"Unknown group name: '{right[0]}'");
                    return;
                }
            }

            try
            {
                Logger.Debug("Saving changes...");
                _dataContext.SaveChanges();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving permissions: {ex.Message}");
                return;
            }

            Logger.Debug($"Permissions added successfully for '{userLogin}'.");
        }

        public void RemoveUserPermissions(string userLogin, IEnumerable<string> rightIds)
        {
            Logger.Debug($"Removing permissions for '{userLogin}'...");

            if (!IsUserExists(userLogin))
            {
                Logger.Error($"The user with login '{userLogin}' does not exist.");
                return;
            }

            foreach (var rightId in rightIds)
            {
                var right = rightId.Split(Delimiter);
                if (right[0] == ItRoleRightGroupName)
                {
                    var userITRole = _dataContext.UserITRoles.FirstOrDefault(ur => ur.UserId == userLogin && ur.RoleId == int.Parse(right[1]));
                    if (userITRole == null)
                    {
                        Logger.Warn($"ITRole '{right[1]}' was not found for user '{userLogin}'.");
                        continue;
                    }

                    _dataContext.UserITRoles.Remove(userITRole);
                    Logger.Debug($"'{right[1]}' was removed successfully.");
                }
                else if (right[0] == RequestRightGroupName)
                {
                    var userRequestRight = _dataContext.UserRequestRights.FirstOrDefault(urr => urr.UserId == userLogin && urr.RightId == int.Parse(right[1]));
                    if (userRequestRight == null)
                    {
                        Logger.Warn($"ITRole '{right[1]}' was not found for user '{userLogin}'.");
                        continue;
                    }

                    _dataContext.UserRequestRights.Remove(userRequestRight);
                    Logger.Debug($"'{right[1]}' was removed successfully.");
                }
                else
                {
                    Logger.Error($"Unknown group name: '{right[0]}'");
                    return;
                }
            }

            try
            {
                Logger.Debug("Saving changes...");
                _dataContext.SaveChanges();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving permissions: {ex.Message}");
                return;
            }

            Logger.Debug($"Permissions removed successfully for '{userLogin}'.");
        }

        public IEnumerable<string> GetUserPermissions(string userLogin)
        {
            Logger.Debug("Getting user permissions...");

            if (!IsUserExists(userLogin))
            {
                Logger.Error($"The user with login '{userLogin}' does not exist.");
                return Enumerable.Empty<string>();
            }

            var userITRoles = _dataContext.ITRoles
                .AsNoTracking()
                .Join(_dataContext.UserITRoles,
                    role => role.Id,
                    userRole => userRole.RoleId,
                    (role, userRole) => new { role, userRole })
                .Where(joined => joined.userRole.UserId == userLogin)
                .Select(joined => $"{ItRoleRightGroupName}{Delimiter}{joined.role.Id}")
                .ToList();

            Logger.Debug($"ITRoles for '{userLogin}': {userITRoles.Count}");

            var userRequestRights = _dataContext.RequestRights
                .AsNoTracking()
                .Join(_dataContext.UserRequestRights,
                    requestRight => requestRight.Id,
                    userRequestRight => userRequestRight.RightId,
                    (requestRight, userRequestRight) => new { requestRight, userRequestRight })
                .Where(joined => joined.userRequestRight.UserId == userLogin)
                .Select(joined => $"{ItRoleRightGroupName}{Delimiter}{joined.requestRight.Id}")
                .ToList();

            Logger.Debug($"RequestRights for '{userLogin}': {userRequestRights.Count}");

            var permissions = userITRoles.Concat(userRequestRights);

            Logger.Debug($"Permissions successfully retrieved for '{userLogin}'.");
            return permissions;
        }

        private User? GetUser(string userLogin)
        {
            var user = _dataContext.Users.FirstOrDefault(u => u.Login == userLogin);
            if (user == null)
            {
                Logger.Error($"The user with login '{userLogin}' does not exist.");
            }

            return user;
        }

        // Uses cached properties of User
        private void SetUserProperties(User user, IEnumerable<UserProperty> userProperties)
        {
            foreach (var userProperty in userProperties)
            {
                var propertyName = userProperty.Name.ToLower();

                if (!_userProperties.TryGetValue(propertyName, out var propertyInfo))
                {
                    Logger.Error($"No property named '{propertyName}'.");
                    return;
                }

                var maxLengthAttribute = propertyInfo.GetCustomAttribute<MaxLengthAttribute>();
                if (maxLengthAttribute != null && !maxLengthAttribute.IsValid(userProperty.Value))
                {
                    Logger.Error($"The value of property '{propertyName}' can not exceed {maxLengthAttribute.Length}.");
                    return;
                }

                var converter = TypeDescriptor.GetConverter(propertyInfo.PropertyType);
                if (converter == null || !converter.CanConvertFrom(typeof(string)))
                {
                    Logger.Error($"Can not convert '{propertyName}' to type '{propertyInfo.DeclaringType}'");
                    return;
                }

                var value = converter.ConvertFrom(userProperty.Value);
                propertyInfo.SetValue(user, value);
            }
        }
    }
}