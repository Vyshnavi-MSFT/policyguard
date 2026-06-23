# Git Quick Reference

A simple guide to the Git commands used in the demo, plus a general workflow to follow whenever you start working.

---

## Commands Used in the Demo

### `git pull`
Downloads the latest changes from the remote (GitHub) and merges them into your current branch.
```bash
git pull
```
Use this to make sure you have the most up-to-date code before you start working.

---

### `git checkout -b <branch-name>`
Creates a **new** branch and switches to it.
```bash
git checkout -b test
```
- `-b` means "create a new branch"
- `test` is the name of the new branch

---

### `git add .`
Stages your changes, getting them ready to be committed. The `.` means "all changed files."
```bash
git add .
```

---

### `git commit -m "<message>"`
Saves a snapshot of your staged changes with a short message describing what you did.
```bash
git commit -m "testing"
```
Keep the message short and descriptive.

---

### `git push -u origin <branch-name>`
Sends your branch to the remote (GitHub) for the **first time** and sets it up to track that branch.
```bash
git push -u origin test
```
- `-u` sets up tracking so next time you can just type `git push`
- `origin` is the name of the remote
- `test` is the branch you're pushing

---

### `git push`
Sends your committed changes to the remote. After the first `push -u`, this is all you need.
```bash
git push
```

---

### `git checkout <branch-name>`
Switches to an **existing** branch.
```bash
git checkout main
```

---

### `git switch <branch-name>`
Also switches to an existing branch (a newer, clearer alternative to `checkout`).
```bash
git switch feature/repo-structure
```

---

### `git fetch`
Downloads the latest info about branches and changes from the remote — but does **not** merge them into your branch.
```bash
git fetch
```
Useful for seeing what's new without changing your current work.

---

### `git branch`
Lists all your local branches. The branch you're currently on is marked with a `*`.
```bash
git branch
```
**Example output:**
```
* main
  test
  feature/repo-structure
```

---

## General Workflow When Getting Started

Follow these steps each time you sit down to work on something new:

```bash
# 1. Switch to the main branch
git checkout main

# 2. Get the latest changes from the team
git pull

# 3. Create a new branch for your work
git checkout -b feature/my-new-feature

# --- Now make your code changes in your editor ---

# 4. Stage your changes
git add .

# 5. Commit your changes with a message
git commit -m "Describe what you changed"

# 6. Push your branch to GitHub (first time)
git push -u origin feature/my-new-feature

# --- Make more changes if needed, then repeat: ---
git add .
git commit -m "Another change"
git push
```

---

## Updating Your Branch When `main` Has Changed

While you're working on your branch, someone else may push new changes to `main`. To bring those changes into your branch, merge `main` into it:

```bash
# 1. Switch to main
git checkout main

# 2. Get the latest changes from the team
git pull

# 3. Switch back to your branch
git checkout branch-name

# 4. Merge the updated main into your branch
git merge main
```

**Why do this?**
- Keeps your branch up to date with the rest of the team's work.
- Reduces the chance of large, painful conflicts later.

**Tip**: Do this regularly so you only deal with small updates at a time. If Git reports a **merge conflict**, it means the same lines were changed in two places — open the affected files, decide which changes to keep, then `git add .` and `git commit` to finish the merge.

---

## Quick Tips

- **Always `git pull` first** so you start with the latest code.
- **Use a new branch** for each feature or task — keep `main` clean.
- **Commit often** with clear messages.
- **Check `git branch`** if you're unsure which branch you're on.
- **Watch for typos** in branch names (e.g. `mian` instead of `main`).
