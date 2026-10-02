// The permissions that are not an entity's CRUD. Each is a constant of LeavePermissions, beside the CRUD ones,
// and an entry of the permission catalogue with its description and category.

// Approving or rejecting a leave request. Whose requests is not in the permission: a manager sees the requests
// of the teams they manage, and nobody else's.
[assembly: Permission("leave.leave-request.decide", "Approve or reject the leave requests one can see", Category = "Leave")]

// Withdrawing one's own leave request before it starts.
[assembly: Permission("leave.leave-request.withdraw", "Withdraw one's own leave request before it starts", Category = "Leave")]

// Seeing one's own profile, balances and personal data, and choosing one's language. Whose profile is not in the
// permission: these operations read the caller and nobody else, so holding it never reaches another employee's
// record.
[assembly: Permission("leave.own-profile.manage", "See one's own profile, balances and personal data, and choose one's language", Category = "Leave")]

// Undoing a termination: the employee is back in every read, and their account works again. A permission of its
// own rather than the entity's update: bringing someone back gives them access again, which is not the same as
// correcting their name. Under leave.employee.*, so HR has it.
[assembly: Permission("leave.employee.restore", "Restore an employee who was recorded as having left", Category = "Employee")]

// Erasing an employee's personal data, once there is no reason left to keep it. A permission of its own, outside
// leave.employee.*: erasure cannot be undone, and nobody should come to hold it as a side effect of being allowed
// to edit employees.
[assembly: Permission("leave.personal-data.erase", "Erase an employee's personal data", Category = "Privacy")]

// Reading the register of processing activities: what personal data the application holds, and how each field is
// erased.
[assembly: Permission("leave.processing-register.read", "Read the register of processing activities", Category = "Privacy")]

// Checking that the audit trail is as it was written: every sealed segment re-hashed and its chain followed.
[assembly: Permission("leave.audit-trail.verify", "Verify the integrity of the audit trail", Category = "Privacy")]

// Looking for security incidents in the trail. A read, and a privileged one: the answer says which
// accounts are being attacked and how hard.
[assembly: Permission("leave.security-incidents.scan", "Scan the audit trail for security incidents", Category = "Privacy")]
