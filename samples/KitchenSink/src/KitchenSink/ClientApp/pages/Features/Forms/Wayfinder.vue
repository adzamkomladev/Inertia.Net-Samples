<script setup lang="ts">
import { Form, Head, Link, router, useForm } from '@inertiajs/vue3';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import InputError from '@/components/InputError.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';
import {
    submitUseForm,
    submitFormComponent,
    toggleFavorite,
    wayfinder as wayfinderAction,
    storeAccount,
} from '@/wayfinder/App/Http/Controllers/Feature/FormController';
import contacts from '@/wayfinder/routes/contacts';
import type { Inertia } from '@/wayfinder/types';

defineProps<Inertia.Pages.Features.Forms.Wayfinder>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Forms' },
    { title: 'Wayfinder' },
];

// Hardcoded useForm
const hardcodedForm = useForm('wayfinder-hardcoded', {
    name: '',
    email: '',
});

// Controller action useForm
const actionForm = useForm('wayfinder-action', {
    name: '',
    email: '',
});

// Named route useForm
const namedForm = useForm('wayfinder-named', {
    name: '',
    email: '',
});

// Precognition useForm
const precogForm = useForm({
    username: '',
    email: '',
    password: '',
    password_confirmation: '',
}).withPrecognition(storeAccount());
</script>

<template>
    <Head title="Wayfinder" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Wayfinder"
                docs="the-basics/routing#generating-urls"
                controller="app/Http/Controllers/Feature/FormController.php#L109"
            >
                Wayfinder generates type-safe TypeScript functions from your
                Laravel routes. Each section compares hardcoded URLs with
                Wayfinder equivalents across Inertia's APIs.
            </FeatureHeader>

            <div class="space-y-10">
                <!-- ============================================ -->
                <!-- SECTION: Hardcoded URLs (Baseline)           -->
                <!-- ============================================ -->
                <div class="space-y-4">
                    <div>
                        <h2 class="text-lg font-semibold tracking-tight">
                            Hardcoded URLs
                        </h2>
                        <p class="text-sm text-muted-foreground">
                            The traditional approach. Works, but fragile when
                            routes change.
                        </p>
                    </div>

                    <div class="grid gap-6 lg:grid-cols-3">
                        <FeatureCard title="useForm">
                            <CodeBlock
                                code="form.submit('post', '/features/forms/use-form')"
                            />

                            <form
                                class="mt-3 space-y-3"
                                @submit.prevent="
                                    hardcodedForm.submit(
                                        'post',
                                        '/features/forms/use-form',
                                        {
                                            preserveScroll: true,
                                            onSuccess: () =>
                                                hardcodedForm.defaults(),
                                        },
                                    )
                                "
                            >
                                <div class="space-y-1">
                                    <Label for="hc-name">Name</Label>
                                    <Input
                                        id="hc-name"
                                        v-model="hardcodedForm.name"
                                        placeholder="Name..."
                                    />
                                    <InputError
                                        :message="hardcodedForm.errors.name"
                                    />
                                </div>
                                <div class="flex items-center gap-2">
                                    <Button
                                        type="submit"
                                        size="sm"
                                        :disabled="hardcodedForm.processing"
                                        >Submit</Button
                                    >
                                    <span
                                        v-if="hardcodedForm.recentlySuccessful"
                                        class="text-sm text-green-600"
                                        >Saved!</span
                                    >
                                </div>
                            </form>
                        </FeatureCard>

                        <FeatureCard title="<Form>">
                            <CodeBlock>
                                <textarea>
                                <Form action="/features/forms/form-component"
                                      method="post">
                                </textarea>
                            </CodeBlock>

                            <Form
                                action="/features/forms/form-component"
                                method="post"
                                preserve-scroll
                                set-defaults-on-success
                                class="mt-3 space-y-3"
                                #default="{
                                    errors,
                                    processing,
                                    recentlySuccessful,
                                }"
                            >
                                <div class="space-y-1">
                                    <Label for="hc-fc-name">Name</Label>
                                    <Input
                                        id="hc-fc-name"
                                        name="name"
                                        placeholder="Name..."
                                    />
                                    <InputError :message="errors.name" />
                                </div>
                                <div class="flex items-center gap-2">
                                    <Button
                                        type="submit"
                                        size="sm"
                                        :disabled="processing"
                                        >Submit</Button
                                    >
                                    <span
                                        v-if="recentlySuccessful"
                                        class="text-sm text-green-600"
                                        >Saved!</span
                                    >
                                </div>
                            </Form>
                        </FeatureCard>

                        <FeatureCard title="router / <Link>">
                            <CodeBlock>
                                <textarea>
                                router.visit('/features/forms/wayfinder')
                                router.post('/features/forms/optimistic-toggle/1', data)

                                <Link href="/contacts">
                                </textarea>
                            </CodeBlock>

                            <div class="mt-3 flex flex-wrap gap-2">
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.visit(
                                            '/features/forms/wayfinder',
                                            { preserveScroll: true },
                                        )
                                    "
                                >
                                    router.visit()
                                </Button>
                                <Button variant="outline" size="sm" as-child>
                                    <Link href="/contacts">Contacts</Link>
                                </Button>
                            </div>
                        </FeatureCard>
                    </div>
                </div>

                <!-- ============================================ -->
                <!-- SECTION: Controller Action Imports            -->
                <!-- ============================================ -->
                <div class="space-y-4">
                    <div>
                        <h2 class="text-lg font-semibold tracking-tight">
                            Controller Actions
                        </h2>
                        <p class="text-sm text-muted-foreground">
                            Import by controller method. Keeps frontend in sync
                            with your PHP controllers.
                        </p>
                        <CodeBlock class="mt-2">
                            <textarea>
                                import { submitUseForm, toggleFavorite } from '@/wayfinder/.../FormController'

                                submitUseForm()        // {"url":"/features/forms/use-form","method":"post"}
                                submitUseForm.url()    // "/features/forms/use-form"
                                toggleFavorite(1)      // {"url":"/features/forms/optimistic-toggle/1","method":"post"}
                                toggleFavorite.url(1)  // "/features/forms/optimistic-toggle/1"
                            </textarea>
                        </CodeBlock>
                    </div>

                    <div class="grid gap-6 lg:grid-cols-3">
                        <FeatureCard
                            title="useForm"
                            description="Pass the action directly to submit(). Method and URL are extracted automatically."
                        >
                            <CodeBlock code="form.submit(submitUseForm())" />

                            <form
                                class="mt-3 space-y-3"
                                @submit.prevent="
                                    actionForm.submit(submitUseForm(), {
                                        preserveScroll: true,
                                        onSuccess: () => actionForm.defaults(),
                                    })
                                "
                            >
                                <div class="space-y-1">
                                    <Label for="ac-name">Name</Label>
                                    <Input
                                        id="ac-name"
                                        v-model="actionForm.name"
                                        placeholder="Name..."
                                    />
                                    <InputError
                                        :message="actionForm.errors.name"
                                    />
                                </div>
                                <div class="flex items-center gap-2">
                                    <Button
                                        type="submit"
                                        size="sm"
                                        :disabled="actionForm.processing"
                                        >Submit</Button
                                    >
                                    <span
                                        v-if="actionForm.recentlySuccessful"
                                        class="text-sm text-green-600"
                                        >Saved!</span
                                    >
                                </div>
                            </form>
                        </FeatureCard>

                        <FeatureCard
                            title="<Form>"
                            description="The :action prop accepts the { url, method } object directly."
                        >
                            <CodeBlock
                                code='<Form :action="submitFormComponent()">'
                            />

                            <Form
                                :action="submitFormComponent()"
                                preserve-scroll
                                set-defaults-on-success
                                class="mt-3 space-y-3"
                                #default="{
                                    errors,
                                    processing,
                                    recentlySuccessful,
                                }"
                            >
                                <div class="space-y-1">
                                    <Label for="ac-fc-name">Name</Label>
                                    <Input
                                        id="ac-fc-name"
                                        name="name"
                                        placeholder="Name..."
                                    />
                                    <InputError :message="errors.name" />
                                </div>
                                <div class="flex items-center gap-2">
                                    <Button
                                        type="submit"
                                        size="sm"
                                        :disabled="processing"
                                        >Submit</Button
                                    >
                                    <span
                                        v-if="recentlySuccessful"
                                        class="text-sm text-green-600"
                                        >Saved!</span
                                    >
                                </div>
                            </Form>
                        </FeatureCard>

                        <FeatureCard
                            title="router / <Link>"
                            description="router and Link both accept { url, method } objects."
                        >
                            <CodeBlock>
                                <textarea>
                                // Extracts method + URL from action
                                router.visit(wayfinderAction())
                                router.visit(toggleFavorite(contact))

                                // Or use .url for string-only APIs
                                router.post(toggleFavorite.url(contact))

                                // Link also accepts object or string
                                <Link :href="contacts.show(id)">
                                <Link :href="contacts.index.url()">
                                </textarea>
                            </CodeBlock>

                            <div class="mt-3 flex flex-wrap gap-2">
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.visit(wayfinderAction(), {
                                            preserveScroll: true,
                                        })
                                    "
                                >
                                    router.visit()
                                </Button>
                                <Button
                                    v-if="sampleContact"
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.visit(
                                            toggleFavorite(sampleContact),
                                            { preserveScroll: true },
                                        )
                                    "
                                >
                                    Toggle favorite
                                </Button>
                                <Button
                                    v-if="sampleContact"
                                    variant="outline"
                                    size="sm"
                                    as-child
                                >
                                    <Link
                                        :href="contacts.show(sampleContact.id)"
                                    >
                                        {{ sampleContact.first_name }}
                                    </Link>
                                </Button>
                            </div>
                        </FeatureCard>
                    </div>
                </div>

                <!-- ============================================ -->
                <!-- SECTION: Named Route Imports                  -->
                <!-- ============================================ -->
                <div class="space-y-4">
                    <div>
                        <h2 class="text-lg font-semibold tracking-tight">
                            Named Routes
                        </h2>
                        <p class="text-sm text-muted-foreground">
                            Import by route name. Mirrors your Laravel route
                            names exactly.
                        </p>
                        <CodeBlock class="mt-2">
                            <textarea>
                                import contacts from '@/wayfinder/routes/contacts'

                                contacts.index()     // {"url":"/contacts","method":"get"}
                                contacts.show(1)     // {"url":"/contacts/1","method":"get"}
                                contacts.show.url(1) // "/contacts/1"
                            </textarea>
                        </CodeBlock>
                    </div>

                    <div class="grid gap-6 lg:grid-cols-3">
                        <FeatureCard
                            title="useForm"
                            description="Named route actions work exactly the same as controller actions."
                        >
                            <CodeBlock code="form.submit(submitUseForm())" />

                            <form
                                class="mt-3 space-y-3"
                                @submit.prevent="
                                    namedForm.submit(submitUseForm(), {
                                        preserveScroll: true,
                                        onSuccess: () => namedForm.defaults(),
                                    })
                                "
                            >
                                <div class="space-y-1">
                                    <Label for="nr-name">Name</Label>
                                    <Input
                                        id="nr-name"
                                        v-model="namedForm.name"
                                        placeholder="Name..."
                                    />
                                    <InputError
                                        :message="namedForm.errors.name"
                                    />
                                </div>
                                <div class="flex items-center gap-2">
                                    <Button
                                        type="submit"
                                        size="sm"
                                        :disabled="namedForm.processing"
                                        >Submit</Button
                                    >
                                    <span
                                        v-if="namedForm.recentlySuccessful"
                                        class="text-sm text-green-600"
                                        >Saved!</span
                                    >
                                </div>
                            </form>
                        </FeatureCard>

                        <FeatureCard
                            title="<Form>"
                            description="Pass the named route action directly to :action."
                        >
                            <CodeBlock
                                code='<Form :action="submitFormComponent()">'
                            />

                            <Form
                                :action="submitFormComponent()"
                                preserve-scroll
                                set-defaults-on-success
                                class="mt-3 space-y-3"
                                #default="{
                                    errors,
                                    processing,
                                    recentlySuccessful,
                                }"
                            >
                                <div class="space-y-1">
                                    <Label for="nr-fc-name">Name</Label>
                                    <Input
                                        id="nr-fc-name"
                                        name="name"
                                        placeholder="Name..."
                                    />
                                    <InputError :message="errors.name" />
                                </div>
                                <div class="flex items-center gap-2">
                                    <Button
                                        type="submit"
                                        size="sm"
                                        :disabled="processing"
                                        >Submit</Button
                                    >
                                    <span
                                        v-if="recentlySuccessful"
                                        class="text-sm text-green-600"
                                        >Saved!</span
                                    >
                                </div>
                            </Form>
                        </FeatureCard>

                        <FeatureCard
                            title="router / <Link>"
                            description="Use named routes for navigation and programmatic visits."
                        >
                            <CodeBlock>
                                <textarea>
                                router.visit(contacts.index())
                                router.visit(contacts.destroy(id))
                                router.prefetch(contacts.show(id))

                                <Link :href="contacts.create()">
                                </textarea>
                            </CodeBlock>

                            <div class="mt-3 flex flex-wrap gap-2">
                                <Button variant="outline" size="sm" as-child>
                                    <Link :href="contacts.index()"
                                        >All contacts</Link
                                    >
                                </Button>
                                <Button variant="outline" size="sm" as-child>
                                    <Link :href="contacts.create()"
                                        >Create contact</Link
                                    >
                                </Button>
                                <Button
                                    v-if="sampleContact"
                                    variant="outline"
                                    size="sm"
                                    as-child
                                >
                                    <Link
                                        :href="contacts.edit(sampleContact.id)"
                                    >
                                        Edit {{ sampleContact.first_name }}
                                    </Link>
                                </Button>
                            </div>
                        </FeatureCard>
                    </div>
                </div>

                <!-- ============================================ -->
                <!-- SECTION: Conventional Forms with .form()      -->
                <!-- ============================================ -->
                <div class="space-y-4">
                    <div>
                        <h2 class="text-lg font-semibold tracking-tight">
                            Conventional Forms
                        </h2>
                        <p class="text-sm text-muted-foreground">
                            The
                            <code class="rounded bg-muted px-1 py-0.5 text-xs"
                                >.form()</code
                            >
                            method returns
                            <code
                                class="rounded bg-muted px-1 py-0.5 text-xs"
                                >{{ '{ action, method }' }}</code
                            >
                            for use with
                            <code class="rounded bg-muted px-1 py-0.5 text-xs"
                                >v-bind</code
                            >
                            on the
                            <code class="rounded bg-muted px-1 py-0.5 text-xs"
                                >&lt;Form&gt;</code
                            >
                            component.
                        </p>
                        <CodeBlock class="mt-2">
                            <textarea>
                                import { submitFormComponent, toggleFavorite } from '@/wayfinder/.../FormController'

                                submitFormComponent.form()  // {"action":"/features/forms/form-component","method":"post"}
                                toggleFavorite.form(1)      // {"action":"/features/forms/optimistic-toggle/1","method":"post"}
                            </textarea>
                        </CodeBlock>
                    </div>

                    <div class="grid gap-6 lg:grid-cols-2">
                        <FeatureCard title="v-bind with .form()">
                            <CodeBlock
                                code='<Form v-bind="submitFormComponent.form()">'
                            />

                            <Form
                                v-bind="submitFormComponent.form()"
                                preserve-scroll
                                set-defaults-on-success
                                class="mt-3 space-y-3"
                                #default="{
                                    errors,
                                    processing,
                                    recentlySuccessful,
                                }"
                            >
                                <div class="space-y-1">
                                    <Label for="cf-name">Name</Label>
                                    <Input
                                        id="cf-name"
                                        name="name"
                                        placeholder="Name..."
                                    />
                                    <InputError :message="errors.name" />
                                </div>
                                <div class="flex items-center gap-2">
                                    <Button
                                        type="submit"
                                        size="sm"
                                        :disabled="processing"
                                        >Submit</Button
                                    >
                                    <span
                                        v-if="recentlySuccessful"
                                        class="text-sm text-green-600"
                                        >Saved!</span
                                    >
                                </div>
                            </Form>
                        </FeatureCard>

                        <FeatureCard title="With route parameters">
                            <CodeBlock>
                                <textarea>
                                // Positional
                                toggleFavorite.form(1)
                                // {"action":"/features/forms/optimistic-toggle/1","method":"post"}

                                // Named
                                toggleFavorite.form({ contact: 1 })
                                // {"action":"/features/forms/optimistic-toggle/1","method":"post"}

                                // Model object
                                toggleFavorite.form({ id: 1 })
                                // {"action":"/features/forms/optimistic-toggle/1","method":"post"}
                                </textarea>
                            </CodeBlock>
                        </FeatureCard>
                    </div>
                </div>

                <!-- ============================================ -->
                <!-- SECTION: Query Parameters                     -->
                <!-- ============================================ -->
                <div class="space-y-4">
                    <div>
                        <h2 class="text-lg font-semibold tracking-tight">
                            Query Parameters
                        </h2>
                        <p class="text-sm text-muted-foreground">
                            Append or merge query params on any Wayfinder route.
                        </p>
                    </div>

                    <div class="grid gap-6 lg:grid-cols-2">
                        <FeatureCard
                            title="query"
                            description="Sets query parameters on the generated URL."
                        >
                            <CodeBlock>
                                <textarea>
                                contacts.index({ query: { search: 'Jane', page: 2 } })
                                // {"url":"/contacts?search=Jane&page=2","method":"get"}

                                toggleFavorite(1, { query: { notify: true } })
                                // {"url":"/features/forms/optimistic-toggle/1?notify=true","method":"post"}
                                </textarea>
                            </CodeBlock>

                            <div class="mt-3 flex flex-wrap gap-2">
                                <Button variant="outline" size="sm" as-child>
                                    <Link
                                        :href="
                                            contacts.index({
                                                query: { search: 'Jane' },
                                            })
                                        "
                                    >
                                        Search "Jane"
                                    </Link>
                                </Button>
                                <Button variant="outline" size="sm" as-child>
                                    <Link
                                        :href="
                                            contacts.index({
                                                query: { favorite: true },
                                            })
                                        "
                                    >
                                        Favorites only
                                    </Link>
                                </Button>
                            </div>
                        </FeatureCard>

                        <FeatureCard
                            title="mergeQuery"
                            description="Merges with current URL params. Pass null to remove a param."
                        >
                            <CodeBlock>
                                <textarea>
                                // Adds page=2 to current URL params
                                contacts.index({ mergeQuery: { page: 2 } })
                                // {"url":"/contacts?page=2","method":"get"}

                                // Removes sort from current URL params
                                contacts.index({ mergeQuery: { sort: null } })
                                // {"url":"/contacts","method":"get"}
                                </textarea>
                            </CodeBlock>

                            <div class="mt-3 flex flex-wrap gap-2">
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.visit(
                                            contacts.index({
                                                mergeQuery: { page: 2 },
                                            }),
                                            {
                                                preserveState: true,
                                                preserveScroll: true,
                                            },
                                        )
                                    "
                                >
                                    Merge page=2
                                </Button>
                            </div>
                        </FeatureCard>
                    </div>
                </div>

                <!-- ============================================ -->
                <!-- SECTION: Precognition                         -->
                <!-- ============================================ -->
                <div class="space-y-4">
                    <div>
                        <h2 class="text-lg font-semibold tracking-tight">
                            Precognition
                        </h2>
                        <p class="text-sm text-muted-foreground">
                            Real-time server-side validation. Pass the Wayfinder
                            URL to
                            <code class="rounded bg-muted px-1 py-0.5 text-xs"
                                >useForm</code
                            >
                            or the
                            <code class="rounded bg-muted px-1 py-0.5 text-xs"
                                >&lt;Form&gt;</code
                            >
                            component. The route must use the
                            <code class="rounded bg-muted px-1 py-0.5 text-xs"
                                >precognitive</code
                            >
                            middleware.
                        </p>
                    </div>

                    <div class="grid gap-6 lg:grid-cols-2">
                        <FeatureCard title="useForm + Precognition">
                            <template #description>
                                Chain
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >.withPrecognition()</code
                                >
                                on
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >useForm</code
                                >
                                and pass a Wayfinder route directly.
                            </template>
                            <CodeBlock>
                                <textarea>
                                const form = useForm({
                                    username: '', email: '', ...
                                }).withPrecognition(storeAccount())

                                // Validate on field change
                                @change="form.validate('username')"

                                // Check field status
                                form.valid('username')
                                form.invalid('username')
                                </textarea>
                            </CodeBlock>

                            <form
                                class="mt-3 space-y-3"
                                @submit.prevent="precogForm.submit()"
                            >
                                <div class="space-y-1">
                                    <Label for="pc-username">Username</Label>
                                    <Input
                                        id="pc-username"
                                        v-model="precogForm.username"
                                        @change="
                                            precogForm.validate('username')
                                        "
                                        :class="{
                                            'border-green-500':
                                                precogForm.valid('username'),
                                            'border-red-500':
                                                precogForm.invalid('username'),
                                        }"
                                        placeholder="3-20 chars, letters/numbers/dashes"
                                    />
                                    <p
                                        v-if="precogForm.invalid('username')"
                                        class="text-sm text-red-600"
                                    >
                                        {{ precogForm.errors.username }}
                                    </p>
                                    <p
                                        v-else-if="precogForm.valid('username')"
                                        class="text-sm text-green-600"
                                    >
                                        Available!
                                    </p>
                                </div>
                                <div class="space-y-1">
                                    <Label for="pc-email">Email</Label>
                                    <Input
                                        id="pc-email"
                                        v-model="precogForm.email"
                                        type="text"
                                        @change="precogForm.validate('email')"
                                        :class="{
                                            'border-green-500':
                                                precogForm.valid('email'),
                                            'border-red-500':
                                                precogForm.invalid('email'),
                                        }"
                                        placeholder="user@example.com"
                                    />
                                    <p
                                        v-if="precogForm.invalid('email')"
                                        class="text-sm text-red-600"
                                    >
                                        {{ precogForm.errors.email }}
                                    </p>
                                </div>
                                <div class="space-y-1">
                                    <Label for="pc-password">Password</Label>
                                    <Input
                                        id="pc-password"
                                        v-model="precogForm.password"
                                        type="password"
                                        @change="
                                            precogForm.validate('password')
                                        "
                                        placeholder="Min 8 characters"
                                    />
                                    <p
                                        v-if="precogForm.invalid('password')"
                                        class="text-sm text-red-600"
                                    >
                                        {{ precogForm.errors.password }}
                                    </p>
                                </div>
                                <div class="space-y-1">
                                    <Label for="pc-confirm"
                                        >Confirm Password</Label
                                    >
                                    <Input
                                        id="pc-confirm"
                                        v-model="
                                            precogForm.password_confirmation
                                        "
                                        type="password"
                                        @change="
                                            precogForm.validate(
                                                'password_confirmation',
                                            )
                                        "
                                    />
                                    <p
                                        v-if="
                                            precogForm.invalid(
                                                'password_confirmation',
                                            )
                                        "
                                        class="text-sm text-red-600"
                                    >
                                        {{
                                            precogForm.errors
                                                .password_confirmation
                                        }}
                                    </p>
                                </div>
                                <div class="flex items-center gap-2">
                                    <Button
                                        type="submit"
                                        size="sm"
                                        :disabled="
                                            precogForm.processing ||
                                            precogForm.validating ||
                                            precogForm.hasErrors
                                        "
                                    >
                                        {{
                                            precogForm.processing
                                                ? 'Creating...'
                                                : precogForm.validating
                                                  ? 'Validating...'
                                                  : 'Create Account'
                                        }}
                                    </Button>
                                </div>
                            </form>
                        </FeatureCard>

                        <FeatureCard title="<Form> + Precognition">
                            <template #description>
                                The
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >&lt;Form&gt;</code
                                >
                                component has built-in precognition support. Use
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >.form()</code
                                >
                                or pass the action object, then add
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >:validation-timeout</code
                                >
                                to enable it.
                            </template>
                            <CodeBlock>
                                <textarea>
                                <Form v-bind="storeAccount.form()"
                                      :validation-timeout="500"
                                      #default="{ validate, valid, invalid }">
                                </textarea>
                            </CodeBlock>

                            <Form
                                v-bind="storeAccount.form()"
                                :validation-timeout="500"
                                class="mt-3 space-y-3"
                                #default="{
                                    errors,
                                    processing,
                                    validating,
                                    validate,
                                    valid,
                                    invalid,
                                    hasErrors,
                                }"
                            >
                                <div class="space-y-1">
                                    <Label for="pcf-username">Username</Label>
                                    <Input
                                        id="pcf-username"
                                        name="username"
                                        @change="validate('username')"
                                        :class="{
                                            'border-green-500':
                                                valid('username'),
                                            'border-red-500':
                                                invalid('username'),
                                        }"
                                        placeholder="3-20 chars, letters/numbers/dashes"
                                    />
                                    <p
                                        v-if="invalid('username')"
                                        class="text-sm text-red-600"
                                    >
                                        {{ errors.username }}
                                    </p>
                                    <p
                                        v-else-if="valid('username')"
                                        class="text-sm text-green-600"
                                    >
                                        Available!
                                    </p>
                                </div>
                                <div class="space-y-1">
                                    <Label for="pcf-email">Email</Label>
                                    <Input
                                        id="pcf-email"
                                        name="email"
                                        type="text"
                                        @change="validate('email')"
                                        :class="{
                                            'border-green-500': valid('email'),
                                            'border-red-500': invalid('email'),
                                        }"
                                        placeholder="user@example.com"
                                    />
                                    <p
                                        v-if="invalid('email')"
                                        class="text-sm text-red-600"
                                    >
                                        {{ errors.email }}
                                    </p>
                                </div>
                                <div class="space-y-1">
                                    <Label for="pcf-password">Password</Label>
                                    <Input
                                        id="pcf-password"
                                        name="password"
                                        type="password"
                                        @change="validate('password')"
                                        placeholder="Min 8 characters"
                                    />
                                    <p
                                        v-if="invalid('password')"
                                        class="text-sm text-red-600"
                                    >
                                        {{ errors.password }}
                                    </p>
                                </div>
                                <div class="space-y-1">
                                    <Label for="pcf-confirm"
                                        >Confirm Password</Label
                                    >
                                    <Input
                                        id="pcf-confirm"
                                        name="password_confirmation"
                                        type="password"
                                        @change="
                                            validate('password_confirmation')
                                        "
                                    />
                                    <p
                                        v-if="invalid('password_confirmation')"
                                        class="text-sm text-red-600"
                                    >
                                        {{ errors.password_confirmation }}
                                    </p>
                                </div>
                                <div class="flex items-center gap-2">
                                    <Button
                                        type="submit"
                                        size="sm"
                                        :disabled="
                                            processing ||
                                            validating ||
                                            hasErrors
                                        "
                                    >
                                        {{
                                            processing
                                                ? 'Creating...'
                                                : validating
                                                  ? 'Validating...'
                                                  : 'Create Account'
                                        }}
                                    </Button>
                                </div>
                            </Form>
                        </FeatureCard>
                    </div>
                </div>
            </div>
        </div>
    </AppLayout>
</template>
