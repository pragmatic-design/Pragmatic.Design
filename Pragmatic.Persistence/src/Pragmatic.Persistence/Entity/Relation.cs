namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Container for relation attributes with semantic nested structure.
/// </summary>
/// <remarks>
///     <para>Usage patterns:</para>
///     <code>
/// // With naming convention (navigation name derived from type):
/// [Relation.OneToMany&lt;OrderLine&gt;]  // → ICollection&lt;OrderLine&gt; OrderLines
///
/// // With explicit navigation name:
/// [Relation.OneToMany&lt;OrderLine&gt;.WithNavigation("Lines", Inverse = "Order")]
///
/// // Multiple relations to same entity (WithNavigation required):
/// [Relation.ManyToOne&lt;User&gt;.WithNavigation("CreatedBy", Inverse = "CreatedOrders")]
/// [Relation.ManyToOne&lt;User&gt;.WithNavigation("AssignedTo", Inverse = "AssignedOrders")]
/// </code>
/// </remarks>
public static class Relation
{
    /// <summary>
    ///     Defines a one-to-one relationship. Use on the dependent side (the one with FK).
    /// </summary>
    /// <typeparam name="TRelated">The related entity type.</typeparam>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class OneToOne<TRelated> : Attribute
        where TRelated : class
    {
        /// <summary>
        ///     Explicit navigation configuration for one-to-one relationship.
        /// </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
        public sealed class WithNavigation : Attribute
        {
            /// <summary>
            ///     Creates a new navigation configuration with the specified name.
            /// </summary>
            /// <param name="name">The name of the navigation property to generate.</param>
            public WithNavigation(string name)
            {
                Name = name;
            }

            /// <summary>
            ///     The name of the navigation property to generate.
            /// </summary>
            public string Name { get; }

            /// <summary>
            ///     The name of the inverse navigation property on the related entity.
            ///     Required when multiple relations exist between the same entities.
            /// </summary>
            public string? Inverse { get; set; }

            /// <summary>
            ///     The foreign key property name. Defaults to {NavigationName}Id.
            /// </summary>
            public string? ForeignKey { get; set; }

            /// <summary>
            ///     The delete behavior for this relationship.
            /// </summary>
            public DeleteBehavior OnDelete { get; set; } = DeleteBehavior.Restrict;

            /// <summary>
            ///     Whether this side is the principal (owns the relationship).
            /// </summary>
            public bool IsPrincipal { get; set; }
        }
    }

    /// <summary>
    ///     Defines a one-to-many relationship. Use on the "one" side (parent).
    /// </summary>
    /// <typeparam name="TRelated">The related entity type (the "many" side).</typeparam>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class OneToMany<TRelated> : Attribute
        where TRelated : class
    {
        /// <summary>
        ///     Explicit navigation configuration for one-to-many relationship.
        /// </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
        public sealed class WithNavigation : Attribute
        {
            /// <summary>
            ///     Creates a new navigation configuration with the specified collection name.
            /// </summary>
            /// <param name="name">The name of the collection navigation property to generate.</param>
            public WithNavigation(string name)
            {
                Name = name;
            }

            /// <summary>
            ///     The name of the collection navigation property to generate.
            /// </summary>
            public string Name { get; }

            /// <summary>
            ///     The name of the inverse navigation property on the related entity.
            ///     Required when multiple relations exist between the same entities.
            /// </summary>
            public string? Inverse { get; set; }

            /// <summary>
            ///     The delete behavior for this relationship.
            /// </summary>
            public DeleteBehavior OnDelete { get; set; } = DeleteBehavior.Cascade;
        }
    }

    /// <summary>
    ///     Defines a many-to-one relationship. Use on the "many" side (child with FK).
    /// </summary>
    /// <typeparam name="TRelated">The related entity type (the "one" side).</typeparam>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ManyToOne<TRelated> : Attribute
        where TRelated : class
    {
        /// <summary>
        ///     Explicit navigation configuration for many-to-one relationship.
        /// </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
        public sealed class WithNavigation : Attribute
        {
            /// <summary>
            ///     Creates a new navigation configuration with the specified name.
            /// </summary>
            /// <param name="name">The name of the navigation property to generate.</param>
            public WithNavigation(string name)
            {
                Name = name;
            }

            /// <summary>
            ///     The name of the navigation property to generate.
            /// </summary>
            public string Name { get; }

            /// <summary>
            ///     The name of the inverse navigation property on the related entity.
            ///     Required when multiple relations exist between the same entities.
            /// </summary>
            public string? Inverse { get; set; }

            /// <summary>
            ///     The foreign key property name. Defaults to {NavigationName}Id.
            /// </summary>
            public string? ForeignKey { get; set; }

            /// <summary>
            ///     Whether the relationship is required. Defaults to based on FK nullability.
            /// </summary>
            public bool Required { get; set; } = true;

            /// <summary>
            ///     The delete behavior for this relationship.
            /// </summary>
            public DeleteBehavior OnDelete { get; set; } = DeleteBehavior.Restrict;
        }
    }

    /// <summary>
    ///     Defines a many-to-many relationship with auto-generated join table.
    /// </summary>
    /// <typeparam name="TRelated">The related entity type.</typeparam>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ManyToMany<TRelated> : Attribute
        where TRelated : class
    {
        /// <summary>
        ///     Explicit navigation configuration for many-to-many relationship.
        /// </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
        public sealed class WithNavigation : Attribute
        {
            /// <summary>
            ///     Creates a new navigation configuration with the specified collection name.
            /// </summary>
            /// <param name="name">The name of the collection navigation property to generate.</param>
            public WithNavigation(string name)
            {
                Name = name;
            }

            /// <summary>
            ///     The name of the collection navigation property to generate.
            /// </summary>
            public string Name { get; }

            /// <summary>
            ///     The name of the inverse navigation property on the related entity.
            ///     Required when multiple relations exist between the same entities.
            /// </summary>
            public string? Inverse { get; set; }

            /// <summary>
            ///     Custom name for the auto-generated join table.
            ///     Defaults to {Entity1}{Entity2} in alphabetical order.
            /// </summary>
            public string? JoinTable { get; set; }
        }
    }

    /// <summary>
    ///     Defines a many-to-many relationship with explicit join entity.
    ///     Use when the join table has additional properties.
    /// </summary>
    /// <typeparam name="TRelated">The related entity type.</typeparam>
    /// <typeparam name="TJoinEntity">The join entity type with additional properties.</typeparam>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ManyToMany<TRelated, TJoinEntity> : Attribute
        where TRelated : class
        where TJoinEntity : class
    {
        /// <summary>
        ///     Explicit navigation configuration for many-to-many relationship with join entity.
        /// </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
        public sealed class WithNavigation : Attribute
        {
            /// <summary>
            ///     Creates a new navigation configuration with the specified collection name.
            /// </summary>
            /// <param name="name">The name of the collection navigation property to generate.</param>
            public WithNavigation(string name)
            {
                Name = name;
            }

            /// <summary>
            ///     The name of the collection navigation property to generate.
            /// </summary>
            public string Name { get; }

            /// <summary>
            ///     The name of the inverse navigation property on the related entity.
            ///     Required when multiple relations exist between the same entities.
            /// </summary>
            public string? Inverse { get; set; }

            /// <summary>
            ///     The navigation property name to the join entity collection.
            ///     Defaults to {JoinEntityName}s.
            /// </summary>
            public string? JoinNavigation { get; set; }

            /// <summary>
            ///     The join entity's property holding the key of <b>this</b> side.
            /// </summary>
            /// <remarks>
            ///     <para>
            ///         Without it EF Core invents shadow foreign keys named after the navigations —
            ///         <c>RelatedToPersistenceId</c> — which the migration never creates, so the join
            ///         entity's table is written with the columns you declared and read with columns
            ///         that do not exist. The first write dies on
            ///         <c>42703: column … does not exist</c>.
            ///     </para>
            ///     <para>
            ///         Named rather than inferred because on a self-reference nothing else can tell the
            ///         two ends apart: both are the same type, and picking by declaration order would
            ///         make the meaning of a relation depend on the order of two lines.
            ///     </para>
            /// </remarks>
            public string? LeftKey { get; set; }

            /// <summary>The join entity's property holding the key of the <b>other</b> side.</summary>
            public string? RightKey { get; set; }
        }
    }
}
